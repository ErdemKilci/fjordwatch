from __future__ import annotations

import pandas as pd
import pytest

from anomaly_detection.features import FEATURE_NAMES, compute_features, features_to_frame


def test_steady_track_has_zero_reversals(steady_track: pd.DataFrame) -> None:
    f = compute_features(steady_track, now_utc=pd.Timestamp("2024-01-01T01:00:00Z"))
    assert f is not None
    assert f.heading_reversals == 0
    assert f.point_count == 60
    assert f.mean_speed_knots == pytest.approx(8.0)


def test_reversing_track_logs_one_reversal(reversing_track: pd.DataFrame) -> None:
    f = compute_features(reversing_track, now_utc=pd.Timestamp("2024-01-01T01:00:00Z"))
    assert f is not None
    assert f.heading_reversals == 1


def test_features_to_frame_columns(steady_track: pd.DataFrame) -> None:
    f = compute_features(steady_track, now_utc=pd.Timestamp("2024-01-01T01:00:00Z"))
    assert f is not None
    df = features_to_frame([f])
    assert list(df.columns) == ["mmsi", *FEATURE_NAMES]
    assert df.shape == (1, len(FEATURE_NAMES) + 1)


def test_returns_none_for_short_track() -> None:
    df = pd.DataFrame({
        "mmsi": [1],
        "ts": [pd.Timestamp("2024-01-01T00:00:00Z")],
        "longitude": [10.0], "latitude": [60.0], "sog_knots": [5.0],
        "cog_deg": [0.0], "heading_deg": [0.0], "nav_status": [0], "msg_type": [1],
    })
    assert compute_features(df, now_utc=pd.Timestamp("2024-01-01T01:00:00Z")) is None


def test_rejects_multiple_mmsis() -> None:
    df = pd.DataFrame({
        "mmsi": [1, 2],
        "ts": [pd.Timestamp("2024-01-01T00:00:00Z"), pd.Timestamp("2024-01-01T00:01:00Z")],
        "longitude": [10.0, 10.1], "latitude": [60.0, 60.1], "sog_knots": [5.0, 5.0],
        "cog_deg": [0.0, 0.0], "heading_deg": [0.0, 0.0], "nav_status": [0, 0], "msg_type": [1, 1],
    })
    with pytest.raises(ValueError, match="exactly one MMSI"):
        compute_features(df, now_utc=pd.Timestamp("2024-01-01T01:00:00Z"))
