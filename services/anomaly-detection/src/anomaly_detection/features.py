"""Trajectory feature engineering.

Each input is a `pandas.DataFrame` of position fixes for a single vessel,
sorted by timestamp ascending, with the columns produced by
`store.read_window`:

    mmsi (int64), ts (datetime64[ns, UTC]), longitude, latitude,
    sog_knots, cog_deg, heading_deg, nav_status, msg_type

The output is a single feature row per vessel.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import pandas as pd

# Coastline distance is a placeholder constant for phase 3. Real coastline
# lookup against Kartverket polygons lands in phase 6 polish; until then we
# emit a fixed value and keep the feature column stable.
PLACEHOLDER_COASTLINE_DISTANCE_KM = 0.0

# A vessel is "stopped" below this speed for the stop-duration feature.
STOPPED_SPEED_KNOTS = 0.5

# Angular bins for trajectory entropy (45 degrees each).
HEADING_BINS = np.linspace(0.0, 360.0, num=9)


@dataclass(frozen=True)
class FeatureRow:
    mmsi: int
    window_start: pd.Timestamp
    window_end: pd.Timestamp
    point_count: int

    mean_speed_knots: float
    std_speed_knots: float
    heading_reversals: int
    stop_seconds: float
    mean_distance_to_coast_km: float
    trajectory_entropy: float
    seconds_since_last_fix: float

    def as_array(self) -> np.ndarray:
        """Project the numerical features into a fixed-order vector."""
        return np.array(
            [
                self.mean_speed_knots,
                self.std_speed_knots,
                float(self.heading_reversals),
                self.stop_seconds,
                self.mean_distance_to_coast_km,
                self.trajectory_entropy,
                self.seconds_since_last_fix,
            ],
            dtype=np.float32,
        )


FEATURE_NAMES: tuple[str, ...] = (
    "mean_speed_knots",
    "std_speed_knots",
    "heading_reversals",
    "stop_seconds",
    "mean_distance_to_coast_km",
    "trajectory_entropy",
    "seconds_since_last_fix",
)


def compute_features(df: pd.DataFrame, *, now_utc: pd.Timestamp) -> FeatureRow | None:
    """Compute the feature row for a single vessel.

    Returns ``None`` if the input has fewer than two distinct timestamps,
    which happens for vessels that have just started broadcasting and offer
    too little signal to score meaningfully.
    """
    if df.empty:
        return None
    if df["mmsi"].nunique() != 1:
        raise ValueError("compute_features expects exactly one MMSI per call")

    df = df.sort_values("ts").reset_index(drop=True)
    if len(df) < 2:
        return None

    sog = df["sog_knots"].astype(float)
    mean_speed = float(np.nanmean(sog))
    std_speed = float(np.nanstd(sog, ddof=0))

    heading = df["heading_deg"].astype(float)
    heading_reversals = _count_heading_reversals(heading)

    stop_seconds = _stop_duration_seconds(df["ts"], sog)
    entropy = _heading_entropy(heading)

    seconds_since_last_fix = float((now_utc - df["ts"].iloc[-1]).total_seconds())

    return FeatureRow(
        mmsi=int(df["mmsi"].iloc[0]),
        window_start=df["ts"].iloc[0],
        window_end=df["ts"].iloc[-1],
        point_count=int(len(df)),
        mean_speed_knots=mean_speed,
        std_speed_knots=std_speed,
        heading_reversals=heading_reversals,
        stop_seconds=stop_seconds,
        mean_distance_to_coast_km=PLACEHOLDER_COASTLINE_DISTANCE_KM,
        trajectory_entropy=entropy,
        seconds_since_last_fix=seconds_since_last_fix,
    )


def _count_heading_reversals(heading: pd.Series) -> int:
    """A "reversal" is a change of more than 90 degrees between successive
    valid headings. Heading is degrees in [0, 360); we wrap differences to the
    shorter arc."""
    valid = heading.dropna().to_numpy()
    if valid.size < 2:
        return 0
    diffs = np.diff(valid)
    # wrap to [-180, 180]
    diffs = (diffs + 180.0) % 360.0 - 180.0
    return int(np.sum(np.abs(diffs) > 90.0))


def _stop_duration_seconds(ts: pd.Series, sog: pd.Series) -> float:
    """Sum of intervals where the vessel was below STOPPED_SPEED_KNOTS at the
    interval's start. Returns seconds."""
    if len(ts) < 2:
        return 0.0
    timestamps = ts.to_numpy()
    speeds = sog.to_numpy()
    intervals = np.diff(timestamps).astype("timedelta64[s]").astype(np.int64)
    is_stopped = (speeds[:-1] < STOPPED_SPEED_KNOTS) & ~np.isnan(speeds[:-1])
    return float(intervals[is_stopped].sum())


def _heading_entropy(heading: pd.Series) -> float:
    """Shannon entropy (natural log) of the binned heading distribution.

    A vessel travelling on one heading has entropy ~0; a vessel taking a
    random walk through every direction approaches log(n_bins).
    """
    valid = heading.dropna().to_numpy()
    if valid.size == 0:
        return 0.0
    counts, _ = np.histogram(valid, bins=HEADING_BINS)
    total = counts.sum()
    if total == 0:
        return 0.0
    p = counts / total
    p = p[p > 0]
    return float(-np.sum(p * np.log(p)))


def features_to_frame(rows: list[FeatureRow]) -> pd.DataFrame:
    """Stack feature rows into a DataFrame indexed by MMSI."""
    if not rows:
        return pd.DataFrame(columns=("mmsi", *FEATURE_NAMES))
    matrix = np.stack([r.as_array() for r in rows])
    out = pd.DataFrame(matrix, columns=list(FEATURE_NAMES))
    out.insert(0, "mmsi", [r.mmsi for r in rows])
    return out
