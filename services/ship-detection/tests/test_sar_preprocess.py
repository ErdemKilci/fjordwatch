from __future__ import annotations

import io

import numpy as np
from PIL import Image

from ship_detection.sar_preprocess import INPUT_SIZE, load_tile, to_model_input


def test_load_tile_returns_float_array() -> None:
    img = Image.new("L", (200, 100), color=128)
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    arr = load_tile(buf.getvalue())
    assert arr.shape == (100, 200)
    assert arr.dtype == np.float32


def test_to_model_input_has_expected_shape() -> None:
    arr = np.zeros((128, 128), dtype=np.float32)
    out = to_model_input(arr)
    assert out.shape == (1, 3, INPUT_SIZE, INPUT_SIZE)
    assert out.dtype == np.float32
