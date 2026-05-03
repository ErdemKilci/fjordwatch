from __future__ import annotations

import numpy as np
import pandas as pd
import pytest

from anomaly_detection.lstm_ae import CHANNELS, T_DEFAULT, resample_trajectory, score, train


@pytest.fixture
def synthetic_sequences() -> np.ndarray:
    rng = np.random.default_rng(0)
    return rng.normal(scale=0.5, size=(16, T_DEFAULT, CHANNELS)).astype(np.float32)


def test_resample_to_fixed_length(steady_track: pd.DataFrame) -> None:
    seq = resample_trajectory(steady_track)
    assert seq.shape == (T_DEFAULT, CHANNELS)
    assert np.isfinite(seq).all()


def test_train_decreases_loss(synthetic_sequences: np.ndarray) -> None:
    model_one_epoch, summary_one = train(synthetic_sequences, epochs=1)
    model_five, summary_five = train(synthetic_sequences, epochs=5)
    assert summary_five.final_loss <= summary_one.final_loss


def test_score_is_unit_interval(synthetic_sequences: np.ndarray) -> None:
    model, _ = train(synthetic_sequences, epochs=2)
    s = score(model, synthetic_sequences)
    assert s.shape == (16,)
    assert np.all(s >= 0.0) and np.all(s <= 1.0)
