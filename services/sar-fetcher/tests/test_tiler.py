from __future__ import annotations

import numpy as np

from sar_fetcher.tiler import _is_blank, _normalize


def test_normalize_returns_uint8_in_range() -> None:
    rng = np.random.default_rng(0)
    arr = rng.uniform(low=0.0, high=10.0, size=(64, 64)).astype(np.float32)
    out = _normalize(arr)
    assert out.dtype == np.uint8
    assert out.min() >= 0
    assert out.max() <= 255


def test_normalize_handles_all_zero() -> None:
    arr = np.zeros((32, 32), dtype=np.float32)
    out = _normalize(arr)
    assert out.shape == (32, 32)


def test_is_blank_detects_zero_arr() -> None:
    assert _is_blank(np.zeros((4, 4), dtype=np.float32))
    assert not _is_blank(np.array([[0, 0], [0, 1]], dtype=np.float32))
