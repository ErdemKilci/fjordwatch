"""SAR tile preprocessing for the YOLOv8 ONNX input."""

from __future__ import annotations

import io

import numpy as np
from PIL import Image

INPUT_SIZE = 640


def load_tile(image_bytes: bytes) -> np.ndarray:
    """Decode a PNG and return a single-channel float32 array."""
    img = Image.open(io.BytesIO(image_bytes)).convert("L")
    return np.asarray(img, dtype=np.float32)


def to_model_input(arr: np.ndarray, *, size: int = INPUT_SIZE) -> np.ndarray:
    """Resize, scale to [0, 1], convert to (1, 3, H, W) like a YOLOv8 input."""
    pil = Image.fromarray(arr.astype(np.uint8), mode="L").resize(
        (size, size), Image.Resampling.BILINEAR
    )
    rescaled = np.asarray(pil, dtype=np.float32) / 255.0
    rgb = np.stack([rescaled, rescaled, rescaled], axis=0)
    return rgb[np.newaxis, ...].astype(np.float32)
