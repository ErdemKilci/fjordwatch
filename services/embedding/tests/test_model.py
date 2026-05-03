from __future__ import annotations

import numpy as np
import pytest

from embedding.model import load_stub_model


def test_stub_returns_unit_norm_vector() -> None:
    model = load_stub_model(dimension=1024)
    vec = model.embed("hello FjordWatch")
    assert vec.shape == (1024,)
    assert vec.dtype == np.float32
    norm = float(np.linalg.norm(vec))
    assert abs(norm - 1.0) < 1e-3


def test_stub_is_deterministic() -> None:
    model = load_stub_model(dimension=64)
    a = model.embed("Norway")
    b = model.embed("Norway")
    np.testing.assert_array_equal(a, b)


def test_stub_distinguishes_inputs() -> None:
    model = load_stub_model(dimension=128)
    a = model.embed("Norway")
    b = model.embed("Sweden")
    # Cosine similarity should be < 1 for different inputs.
    cos = float(np.dot(a, b))
    assert cos < 0.9999


def test_empty_input_rejected() -> None:
    model = load_stub_model(dimension=64)
    with pytest.raises(ValueError, match="non-empty"):
        model.embed("")
