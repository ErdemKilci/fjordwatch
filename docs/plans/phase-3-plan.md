# Phase 3 plan — Anomaly detection

## Goal
A vessel-level anomaly score is computed every 10 minutes, stored in
`vessel_anomalies`, and surfaced to the operator via `GET /anomalies` plus a
new "Anomalies" tab on the Blazor frontend. The ensemble combines an Isolation
Forest on engineered tabular features with an LSTM autoencoder reconstruction
error on resampled trajectory windows.

## Files to create

### Python service (`services/anomaly-detection/`)
1. `pyproject.toml` — `fastapi`, `uvicorn`, `numpy`, `pandas`, `scikit-learn`, `torch` (CPU-only build), `onnx`, `onnxruntime`, `psycopg[binary]`, `redis`, `pydantic-settings`, `mlflow-skinny`, `prometheus-fastapi-instrumentator`, `apscheduler`, dev: `pytest`, `pytest-asyncio`, `ruff`, `mypy`, `httpx`.
2. `src/anomaly_detection/__init__.py`
3. `src/anomaly_detection/config.py` — pydantic-settings reading `DATABASE_URL`, `REDIS_URL`, `MODEL_DIR`, `MLFLOW_TRACKING_URI`, scheduler interval, batch size.
4. `src/anomaly_detection/features.py` — `compute_features(df)` returning a feature row per MMSI for a given window: mean speed, std speed, heading-reversal count, total stop seconds, mean distance from coast (placeholder-constant for phase 3, real coastline lookup is phase 6 polish), trajectory entropy, time since last fix.
5. `src/anomaly_detection/isoforest.py` — `IsoForestScorer` wraps `sklearn.ensemble.IsolationForest`. `fit(df)`, `score(df)` returning a normalized 0..1 score where 1 is most anomalous.
6. `src/anomaly_detection/lstm_ae.py` — `LstmAutoencoder` PyTorch module + train/score helpers. `score(seq)` returns reconstruction error per sample.
7. `src/anomaly_detection/ensemble.py` — `EnsembleScorer` weighted blend of IsoForest + LSTM-AE; reports per-feature contribution from IsoForest path lengths.
8. `src/anomaly_detection/store.py` — async Postgres reader for the 6-hour window per vessel and async writer to `vessel_anomalies`.
9. `src/anomaly_detection/scheduler.py` — APScheduler async job that runs every 10 minutes, scores every active vessel, writes results.
10. `src/anomaly_detection/api.py` — FastAPI app: `POST /score`, `GET /healthz`, `GET /readyz`, Prometheus `/metrics`.
11. `src/anomaly_detection/main.py` — entrypoint that starts the scheduler and uvicorn.
12. `scripts/train.py` — CLI: pull a recent window from Postgres, fit both models, register run + artifacts in MLflow, write `models/isoforest.pkl` + `models/lstm_ae.onnx`.
13. `scripts/inject_synthetic_anomalies.py` — generate synthetic vessel trajectories with known anomalies for offline evaluation.
14. `tests/test_features.py`, `tests/test_isoforest.py`, `tests/test_lstm_ae.py`, `tests/test_ensemble.py`, `tests/test_api.py`, `tests/conftest.py`.
15. `services/anomaly-detection/Dockerfile` — multi-stage `python:3.12-slim` build, runs as non-root, healthcheck on `/healthz`.
16. `services/anomaly-detection/README.md`.

### Database
17. `services/db/migrations/V2__anomaly_indexes.sql` — refine `vessel_anomalies` indexes for the new `since` + `min_score` query, add `processed_window_end` if needed.

### Core API extension
18. `FjordWatch.Domain/Anomaly.cs` — `Anomaly` record + `IAnomalyRepository`.
19. `FjordWatch.Infrastructure/PostgresAnomalyRepository.cs` — Dapper read against `vessel_anomalies`.
20. `FjordWatch.Api/Endpoints/AnomalyEndpoints.cs` — `GET /anomalies?since=&min_score=&limit=`. Tests in `FjordWatch.Api.Tests/Endpoints/AnomalyEndpointsTests.cs`.

### Frontend
21. `services/web/FjordWatch.Web/Pages/Anomalies.razor` — sortable MudBlazor data grid. Click → `NavigationManager` with query string that the Home page consumes to focus the map and replay the suspicious window via the existing track endpoint.

### CI + compose
22. `.github/workflows/python.yml` — path-filtered. Steps: `ruff check`, `ruff format --check`, `mypy --strict`, `pytest`.
23. `docker-compose.yml` — replace `anomaly-detection` busybox stub with the real build; depend on `db-migrate`, `postgres`, `redis`. Add an `mlflow` service running `mlflow server` against MinIO + Postgres for tracking.

### Docs
24. `docs/adr/0002-isoforest-lstm-ae-ensemble.md` — record the ensemble choice.
25. `docs/adr/0003-mlflow-tracking-stack.md` — record the MLflow + MinIO + Postgres tracking deployment.

## Deviations from spec

- **Coastline distance is a placeholder constant in phase 3.** Spec lists "mean distance from nearest coastline" as a feature. Computing it correctly requires an offline pipeline that ingests Norwegian coastline polygons (e.g., from Kartverket) and a precomputed distance grid. That's a large dependency for one feature; phase 6 polish wires it. The placeholder is set to 0 and excluded from IsoForest's contributing features so the score is not biased.
- **LSTM-AE trained on synthetic windows in CI.** Spec calls for "trained on a baseline week of normal Norwegian traffic". A baseline week of real data is not in the dev environment; `scripts/train.py` runs against whatever data is in Postgres at training time, with `--synthetic-fallback` that generates a corpus of plausible Norwegian coastal trajectories when the live-data window is too small. The synthetic fallback is documented and gated behind a CLI flag so it's not used silently.
- **MLflow as a sidecar `mlflow-skinny` server, not a full standalone deployment.** Reduces moving parts for the local dev compose; the SQLite + MinIO backend is the same backend Azure ML's MLflow flavor uses, so the client code is portable.
- **Scoring batch is sequential per worker.** The spec doesn't dictate; for a portfolio piece a single worker scoring ~10000 active vessels every 10 minutes fits inside one container without parallelism. Phase 6 adds a `concurrency` knob if needed.
- **No "replay the suspicious window" yet.** The Anomalies page links to `/?focus={mmsi}&from={t0}&to={t1}` and the map page draws the track for that window. A real replay (timeline scrubber, marker animation along the line) is left for phase 6 polish since it's a UX/JS interop concern, not an ML concern.

## Verification

| Gate | How |
|---|---|
| `ruff check`, `ruff format --check`, `mypy --strict` | All clean. |
| `pytest` | Green. Tests cover feature shape, IsoForest fit/score determinism with a fixed seed, LSTM-AE training loss decreases over 5 epochs on a synthetic batch, ensemble blends correctly, `/score` endpoint returns the documented shape. |
| Synthetic anomaly evaluation | `scripts/inject_synthetic_anomalies.py` generates 100 normal + 20 anomalous trajectories; the ensemble flags ≥ 95 % of injected anomalies above the 90th percentile of the normal scores. |
| `docker compose up anomaly-detection` | Reaches healthy. Scheduler logs a first scoring tick within 10 minutes (or 30 s when `SCORER_INTERVAL_SECONDS` is overridden for the smoke test). |
| `GET /anomalies` from core-api | Returns the rows the Python service has written. |
| Anomalies page in the UI | Lists rows sorted by score descending; click navigates to the map focused on the vessel. |

## Risks

- **Cold-start scoring with no training data.** Until `scripts/train.py` runs, the ensemble has no model. The service ships a small pretrained IsoForest checkpoint (fit on synthetic data) as a fallback so the first run isn't a hard error. This is documented in `services/anomaly-detection/README.md`.
- **Postgres write contention.** `vessel_anomalies` insert + index maintenance during a scoring batch could spike. Mitigation: scorer uses `INSERT ... ON CONFLICT DO NOTHING` with the `(mmsi, window_end)` natural key from V2.
- **MLflow + MinIO compose creep.** Adding two more services to compose increases first-time `make up` to maybe 90 s. Flagged as acceptable for phase 3; phase 6 adds a `make up-min` profile that excludes ML services for backend-only iteration.
- **Spec gate "5 plausible anomalies on a 24h replay" requires real data.** The CI gate is the synthetic-injection test (95 % above 90th percentile). The 24h replay gate is a manual step the developer runs once on first live capture.
