"""YOLOv8 ONNX inference wrapper.

Ships a placeholder mode that returns no detections when the real
``yolov8_ship.onnx`` artifact is missing, so the service can boot and
expose the API contract before training has run. Real model files come
from running ``scripts/train.py`` or downloading a pretrained ONNX export.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .sar_preprocess import INPUT_SIZE, to_model_input

logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class Detection:
    bbox_pixels: tuple[float, float, float, float]  # x1, y1, x2, y2 in tile coords
    confidence: float
    class_id: int


class ShipDetector:
    def __init__(self, model_path: Path, *, confidence_threshold: float = 0.25) -> None:
        self._model_path = model_path
        self._confidence_threshold = confidence_threshold
        self._session: object | None = None
        self._input_name: str | None = None

    def warm_up(self) -> None:
        if not self._model_path.exists():
            logger.warning("model %s missing; service will return zero detections", self._model_path)
            return
        try:
            import onnxruntime as ort

            self._session = ort.InferenceSession(
                str(self._model_path),
                providers=["CPUExecutionProvider"],
            )
            self._input_name = self._session.get_inputs()[0].name  # type: ignore[union-attr]
        except Exception:
            logger.exception("onnx session init failed; service will return zero detections")

    def detect(self, tile_arr: np.ndarray) -> list[Detection]:
        """Run inference. ``tile_arr`` is a single-channel uint8/float array."""
        if self._session is None or self._input_name is None:
            return []
        x = to_model_input(tile_arr, size=INPUT_SIZE)
        outputs = self._session.run(None, {self._input_name: x})  # type: ignore[union-attr]
        return self._parse_outputs(outputs[0], tile_h=tile_arr.shape[0], tile_w=tile_arr.shape[1])

    def _parse_outputs(self, raw: np.ndarray, *, tile_h: int, tile_w: int) -> list[Detection]:
        """Decode the canonical YOLOv8 ONNX output (1, 84, N) into bbox lists.

        For a placeholder ONNX with arbitrary shape we return an empty list.
        """
        try:
            arr = np.squeeze(raw)
            if arr.ndim != 2 or arr.shape[0] < 5:
                return []
            xywh = arr[:4]
            scores = arr[4:]
            best_class = scores.argmax(axis=0)
            best_score = scores.max(axis=0)
            keep = best_score >= self._confidence_threshold
            results: list[Detection] = []
            sx = tile_w / INPUT_SIZE
            sy = tile_h / INPUT_SIZE
            for i in np.flatnonzero(keep):
                cx, cy, w, h = (float(v) for v in xywh[:, i])
                x1 = max(0.0, (cx - w / 2.0) * sx)
                y1 = max(0.0, (cy - h / 2.0) * sy)
                x2 = min(float(tile_w), (cx + w / 2.0) * sx)
                y2 = min(float(tile_h), (cy + h / 2.0) * sy)
                results.append(
                    Detection(
                        bbox_pixels=(x1, y1, x2, y2),
                        confidence=float(best_score[i]),
                        class_id=int(best_class[i]),
                    )
                )
            return results
        except Exception:
            logger.exception("output decode failed; treating as no-detections")
            return []
