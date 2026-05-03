"""Offline trainer: fit the IsoForest + LSTM-AE ensemble.

Reads recent positions from Postgres (or generates synthetic data when the
live window is too small), trains both models, optionally registers the run
in MLflow, and writes the artifacts to MODEL_DIR.

Usage::

    python -m anomaly_detection.scripts.train --window-hours 168
    python -m anomaly_detection.scripts.train --synthetic-fallback
"""

from __future__ import annotations

import argparse
import asyncio
import logging
from datetime import timedelta
from pathlib import Path

import numpy as np
import pandas as pd

from anomaly_detection.config import get_settings
from anomaly_detection.features import compute_features, features_to_frame
from anomaly_detection.isoforest import IsoForestScorer
from anomaly_detection.lstm_ae import T_DEFAULT, export_onnx, resample_trajectory, train as train_lstm
from anomaly_detection.store import open_pool, list_active_vessels, read_window, utc_now

logger = logging.getLogger(__name__)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Train the FjordWatch anomaly ensemble")
    parser.add_argument("--window-hours", type=int, default=168)
    parser.add_argument("--epochs", type=int, default=5)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--synthetic-fallback", action="store_true")
    parser.add_argument("--mlflow", action="store_true")
    return parser.parse_args()


async def _gather_real_data(window_hours: int) -> tuple[list, np.ndarray]:
    settings = get_settings()
    conn = await open_pool(settings.database_url)
    feature_rows = []
    sequences: list[np.ndarray] = []
    try:
        since = utc_now() - timedelta(hours=window_hours)
        for mmsi in await list_active_vessels(conn, since=since):
            df = await read_window(conn, mmsi=mmsi, since=since)
            if len(df) < 10:
                continue
            features = compute_features(df, now_utc=utc_now())
            if features is None:
                continue
            feature_rows.append(features)
            sequences.append(resample_trajectory(df))
    finally:
        await conn.close()
    if sequences:
        return feature_rows, np.stack(sequences)
    return [], np.zeros((0, T_DEFAULT, 4), dtype=np.float32)


def _generate_synthetic(seed: int, n: int = 200) -> tuple[list, np.ndarray]:
    """Build a synthetic baseline of plausible Norwegian coastal trajectories."""
    rng = np.random.default_rng(seed)
    feature_rows = []
    sequences = []
    base_now = pd.Timestamp.utcnow()
    for i in range(n):
        steps = rng.integers(40, 120)
        ts = pd.date_range(end=base_now, periods=int(steps), freq="60s", tz="UTC")
        base_lon = rng.uniform(5.0, 25.0)
        base_lat = rng.uniform(58.0, 71.0)
        heading = rng.uniform(0, 360)
        lons = base_lon + np.cumsum(rng.normal(0, 0.001, steps))
        lats = base_lat + np.cumsum(rng.normal(0, 0.001, steps))
        sog = np.clip(rng.normal(8.0, 1.5, steps), 0.0, 25.0)
        df = pd.DataFrame({
            "mmsi": np.full(steps, 200_000_000 + i, dtype=np.int64),
            "ts": ts,
            "longitude": lons,
            "latitude": lats,
            "sog_knots": sog,
            "cog_deg": np.full(steps, heading),
            "heading_deg": np.full(steps, heading),
            "nav_status": np.zeros(steps, dtype=np.int16),
            "msg_type": np.full(steps, 1, dtype=np.int16),
        })
        feature_row = compute_features(df, now_utc=base_now)
        if feature_row is None:
            continue
        feature_rows.append(feature_row)
        sequences.append(resample_trajectory(df))
    return feature_rows, np.stack(sequences)


def main() -> None:
    args = parse_args()
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    settings = get_settings()
    settings.model_dir.mkdir(parents=True, exist_ok=True)

    feature_rows, sequences = asyncio.run(_gather_real_data(args.window_hours))
    if len(feature_rows) < 50:
        if not args.synthetic_fallback:
            raise SystemExit(
                f"only {len(feature_rows)} eligible vessels in the live window; pass "
                "--synthetic-fallback to train against generated data."
            )
        logger.warning("falling back to synthetic data; real-data fit was %d rows", len(feature_rows))
        feature_rows, sequences = _generate_synthetic(args.seed)

    df = features_to_frame(feature_rows)
    iso = IsoForestScorer(random_state=args.seed).fit(df)
    iso.save(settings.model_dir / "isoforest.pkl")

    model, summary = train_lstm(sequences, epochs=args.epochs, seed=args.seed)
    export_onnx(model, settings.model_dir / "lstm_ae.onnx")

    logger.info(
        "training complete: rows=%d epochs=%d final_loss=%.4f score_p90=%.4f",
        len(feature_rows),
        summary.epochs,
        summary.final_loss,
        summary.score_p90,
    )

    if args.mlflow and settings.mlflow_tracking_uri:
        import mlflow

        mlflow.set_tracking_uri(settings.mlflow_tracking_uri)
        mlflow.set_experiment(settings.mlflow_experiment)
        with mlflow.start_run(run_name="ensemble-fit"):
            mlflow.log_param("epochs", summary.epochs)
            mlflow.log_param("seed", args.seed)
            mlflow.log_param("rows", len(feature_rows))
            mlflow.log_metric("final_loss", summary.final_loss)
            mlflow.log_metric("score_p50", summary.score_p50)
            mlflow.log_metric("score_p90", summary.score_p90)
            mlflow.log_metric("score_p99", summary.score_p99)
            mlflow.log_artifact(str(settings.model_dir / "isoforest.pkl"))
            mlflow.log_artifact(str(settings.model_dir / "lstm_ae.onnx"))


if __name__ == "__main__":
    main()
