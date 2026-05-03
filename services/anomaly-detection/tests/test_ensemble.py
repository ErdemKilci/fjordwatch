from __future__ import annotations

import numpy as np
import pandas as pd

from anomaly_detection.ensemble import EnsembleScorer
from anomaly_detection.features import FEATURE_NAMES, FeatureRow
from anomaly_detection.isoforest import IsoForestScorer


def _make_feature_row(mmsi: int, *, mean_speed: float = 8.0, reversals: int = 0) -> FeatureRow:
    return FeatureRow(
        mmsi=mmsi,
        window_start=pd.Timestamp("2024-01-01T00:00:00Z"),
        window_end=pd.Timestamp("2024-01-01T01:00:00Z"),
        point_count=60,
        mean_speed_knots=mean_speed,
        std_speed_knots=0.5,
        heading_reversals=reversals,
        stop_seconds=0.0,
        mean_distance_to_coast_km=0.0,
        trajectory_entropy=0.5,
        seconds_since_last_fix=30.0,
    )


def test_ensemble_returns_one_result_per_input() -> None:
    rng = np.random.default_rng(0)
    train_df = pd.DataFrame(rng.normal(size=(128, len(FEATURE_NAMES))), columns=list(FEATURE_NAMES))
    iso = IsoForestScorer(random_state=0).fit(train_df)
    scorer = EnsembleScorer(iso=iso, lstm=None)

    rows = [_make_feature_row(257_000_001), _make_feature_row(257_000_002, reversals=10)]
    results = scorer.score(rows, sequences=None)
    assert len(results) == 2
    assert {r.mmsi for r in results} == {257_000_001, 257_000_002}
    for r in results:
        assert 0.0 <= r.score <= 1.0
        assert set(r.contributing.keys()) == set(FEATURE_NAMES)


def test_invalid_weights_rejected() -> None:
    rng = np.random.default_rng(0)
    train_df = pd.DataFrame(rng.normal(size=(64, len(FEATURE_NAMES))), columns=list(FEATURE_NAMES))
    iso = IsoForestScorer(random_state=0).fit(train_df)
    import pytest
    with pytest.raises(ValueError, match="weights must sum to 1"):
        EnsembleScorer(iso=iso, lstm=None, iso_weight=0.7, lstm_weight=0.4)
