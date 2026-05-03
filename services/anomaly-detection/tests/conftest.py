from __future__ import annotations

import numpy as np
import pandas as pd
import pytest


@pytest.fixture
def steady_track() -> pd.DataFrame:
    """A 60-step steady-heading northbound trajectory at ~8 knots."""
    n = 60
    ts = pd.date_range("2024-01-01T00:00:00Z", periods=n, freq="60s", tz="UTC")
    return pd.DataFrame({
        "mmsi": np.full(n, 257_000_001, dtype=np.int64),
        "ts": ts,
        "longitude": np.linspace(10.0, 10.05, n),
        "latitude": np.linspace(60.0, 60.05, n),
        "sog_knots": np.full(n, 8.0, dtype=np.float32),
        "cog_deg": np.full(n, 0.0, dtype=np.float32),
        "heading_deg": np.full(n, 0.0, dtype=np.float32),
        "nav_status": np.zeros(n, dtype=np.int16),
        "msg_type": np.full(n, 1, dtype=np.int16),
    })


@pytest.fixture
def reversing_track() -> pd.DataFrame:
    """Same shape as the steady track but flips heading 180° halfway."""
    n = 60
    ts = pd.date_range("2024-01-01T00:00:00Z", periods=n, freq="60s", tz="UTC")
    headings = np.concatenate([np.zeros(n // 2), np.full(n - n // 2, 180.0)])
    return pd.DataFrame({
        "mmsi": np.full(n, 257_000_002, dtype=np.int64),
        "ts": ts,
        "longitude": np.linspace(10.0, 10.05, n),
        "latitude": np.linspace(60.0, 60.05, n),
        "sog_knots": np.full(n, 8.0, dtype=np.float32),
        "cog_deg": headings,
        "heading_deg": headings,
        "nav_status": np.zeros(n, dtype=np.int16),
        "msg_type": np.full(n, 1, dtype=np.int16),
    })
