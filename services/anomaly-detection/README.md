# anomaly-detection (Python 3.12, FastAPI)

Trajectory-anomaly scorer for FjordWatch. Reads recent vessel positions from
Postgres, computes engineered features over a configurable window, runs an
Isolation Forest + LSTM autoencoder ensemble, and writes results to
`vessel_anomalies` for the .NET core API to surface in the UI.

## Pipeline

```
Postgres positions (last 6 h per vessel)
    -> features.compute_features
        -> IsolationForest tabular score
        -> LSTM-AE reconstruction error (resampled 64-step trajectory)
            -> EnsembleScorer (weighted blend, default 0.6/0.4)
                -> Postgres vessel_anomalies (insert above SCORE_FLOOR)
```

The same scorer is exposed via `POST /score` for ad-hoc querying without
waiting for the next scheduler tick.

## Run locally

The service auto-bootstraps a synthetic-fit IsoForest on first start, so it
answers `/score` even before training has run. To get real scores:

```bash
make up                                              # brings up Postgres + the service
docker compose run --rm anomaly-detection \
    python -m anomaly_detection.scripts.train --synthetic-fallback
```

## Endpoints

| Method | Path | Returns |
|---|---|---|
| GET | `/healthz` | `200 ok` once the process is alive. |
| GET | `/readyz` | `200 ready` when Postgres responds. |
| GET | `/metrics` | Prometheus exposition. |
| POST | `/score` | `{ score, iso_score, lstm_score, contributing[], model_versions }` for one MMSI. |

## Test

```bash
cd services/anomaly-detection
pip install -e ".[dev]" --extra-index-url https://download.pytorch.org/whl/cpu
ruff check .
ruff format --check .
mypy src
pytest
```

CI runs the same four commands on every push and PR via
`.github/workflows/python.yml`.

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `DATABASE_URL` | required | Postgres DSN. |
| `REDIS_URL` | `redis://redis:6379/0` | Redis URL (reserved for future use). |
| `MODEL_DIR` | `/app/models` | Where trained artifacts live. |
| `MLFLOW_TRACKING_URI` | unset | When set, training runs are registered. |
| `ANOMALY_WINDOW_MINUTES` | `360` | Window per vessel. |
| `SCORER_INTERVAL_SECONDS` | `600` | Scheduler tick. Override to `30` for smoke tests. |
| `MIN_POSITIONS_PER_WINDOW` | `10` | Skip vessels with fewer fixes. |
| `SCORE_FLOOR` | `0.05` | Suppress writes for quiet vessels. |
| `ANOMALY_METRICS_PORT` | `8002` | HTTP listen port. |
| `LOG_LEVEL` | `INFO` | Standard Python log level. |

## Synthetic-anomaly evaluation

`scripts/inject_synthetic_anomalies.py` generates 100 normal + 20 anomalous
trajectories. The phase 3 acceptance gate is that the ensemble flags ≥ 95 %
of injected anomalies above the 90th percentile of normal scores. The CI
workflow runs this offline; the 24-hour real-data replay gate is a manual
step the developer runs after recording a coastal sample.
