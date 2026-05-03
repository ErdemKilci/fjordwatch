"""Generate synthetic baseline + anomalous trajectories for offline evaluation.

Used by the synthetic-anomaly evaluation gate in phase 3:
- 100 normal trajectories at steady speed and stable heading.
- 20 anomalous trajectories that include sudden 180° heading reversals,
  abrupt stops in mid-sea, or implausible coastal-jump positions.

Writes a CSV pair to ``--out`` for use in tests or offline evaluation.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import pandas as pd

NORMAL_COUNT = 100
ANOMALY_COUNT = 20
STEPS = 80


@dataclass(frozen=True)
class Trajectory:
    label: str  # "normal" | "anomalous"
    df: pd.DataFrame


def _normal(rng: np.random.Generator, mmsi: int, base_now: pd.Timestamp) -> Trajectory:
    ts = pd.date_range(end=base_now, periods=STEPS, freq="60s", tz="UTC")
    base_lon = rng.uniform(5.0, 25.0)
    base_lat = rng.uniform(58.0, 71.0)
    heading = rng.uniform(0, 360)
    lons = base_lon + np.cumsum(rng.normal(0, 0.001, STEPS))
    lats = base_lat + np.cumsum(rng.normal(0, 0.001, STEPS))
    sog = np.clip(rng.normal(8.0, 1.0, STEPS), 0.0, 20.0)
    df = pd.DataFrame(
        {
            "mmsi": np.full(STEPS, mmsi, dtype=np.int64),
            "ts": ts,
            "longitude": lons,
            "latitude": lats,
            "sog_knots": sog,
            "cog_deg": np.full(STEPS, heading),
            "heading_deg": np.full(STEPS, heading),
            "nav_status": np.zeros(STEPS, dtype=np.int16),
            "msg_type": np.full(STEPS, 1, dtype=np.int16),
        }
    )
    return Trajectory("normal", df)


def _anomalous(rng: np.random.Generator, mmsi: int, base_now: pd.Timestamp) -> Trajectory:
    base = _normal(rng, mmsi, base_now)
    df = base.df.copy()
    flavor = rng.choice(["heading-reversal", "midsea-stop", "coastal-jump"])
    if flavor == "heading-reversal":
        df.loc[df.index[STEPS // 2 :], "heading_deg"] = (df["heading_deg"].iloc[0] + 180) % 360
        df.loc[df.index[STEPS // 2 :], "cog_deg"] = (df["cog_deg"].iloc[0] + 180) % 360
    elif flavor == "midsea-stop":
        df.loc[df.index[STEPS // 3 : 2 * STEPS // 3], "sog_knots"] = 0.0
    else:  # coastal-jump
        df.loc[df.index[STEPS // 2], "longitude"] += 5.0
        df.loc[df.index[STEPS // 2], "latitude"] += 2.0
    return Trajectory("anomalous", df)


def generate(seed: int) -> list[Trajectory]:
    rng = np.random.default_rng(seed)
    base_now = pd.Timestamp.utcnow()
    out: list[Trajectory] = []
    for i in range(NORMAL_COUNT):
        out.append(_normal(rng, 200_000_000 + i, base_now))
    for i in range(ANOMALY_COUNT):
        out.append(_anomalous(rng, 300_000_000 + i, base_now))
    return out


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    trajectories = generate(args.seed)
    rows = []
    for t in trajectories:
        for _, r in t.df.iterrows():
            rows.append({**r.to_dict(), "label": t.label})
    pd.DataFrame(rows).to_csv(args.out / "synthetic.csv", index=False)


if __name__ == "__main__":
    main()
