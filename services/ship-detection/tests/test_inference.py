from __future__ import annotations

from pathlib import Path

import numpy as np

from ship_detection.inference import ShipDetector


def test_missing_model_returns_no_detections(tmp_path: Path) -> None:
    detector = ShipDetector(model_path=tmp_path / "missing.onnx")
    detector.warm_up()  # should warn and not raise
    arr = np.zeros((128, 128), dtype=np.float32)
    assert detector.detect(arr) == []
