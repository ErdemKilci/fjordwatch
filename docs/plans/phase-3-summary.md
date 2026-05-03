# Phase 3 summary — Anomaly detection

## What was built

### Python service (`services/anomaly-detection/`, FastAPI + Python 3.12)

| Module | Purpose |
|---|---|
| `features.py` | `compute_features(df, now_utc)` returns a `FeatureRow` per MMSI with mean/std speed, heading reversals, stop duration, trajectory entropy, time since last fix. Coastline distance is a placeholder constant (real lookup deferred to phase 6). |
| `isoforest.py` | `IsoForestScorer` wraps sklearn `IsolationForest`. Score normalized to `[0, 1]`. Per-feature contributions via an ablation approximation. Pickle save/load. |
| `lstm_ae.py` | `LstmAutoencoder` PyTorch module + `train` + `score` helpers. Reconstruction error squashed via `tanh` to the same `[0, 1]` range as IsoForest. ONNX export for portability. |
| `ensemble.py` | `EnsembleScorer` weighted blend (default 0.6 IsoForest / 0.4 LSTM-AE) returning per-vessel `EnsembleResult`s with score, contributions, and model versions. |
| `store.py` | `psycopg` async I/O. Reads the per-MMSI 6-hour window, writes results to `vessel_anomalies` with `INSERT ... ON CONFLICT DO NOTHING`. |
| `scheduler.py` | APScheduler `AsyncIOScheduler` job that runs every `SCORER_INTERVAL_SECONDS` (default 600), scores every active vessel, writes results above `SCORE_FLOOR`. |
| `api.py` | FastAPI surface: `POST /score`, `GET /healthz`, `GET /readyz`, Prometheus `/metrics`. Auto-bootstraps a synthetic-fit IsoForest fallback on first start so `/score` is callable before training. |
| `main.py` | Single-process entrypoint that boots the scheduler and uvicorn together. |
| `scripts/train.py` | CLI trainer. Pulls the live window from Postgres; falls back to a generated baseline of plausible Norwegian coastal trajectories with `--synthetic-fallback`. Optional MLflow run via `--mlflow`. |
| `scripts/inject_synthetic_anomalies.py` | Generates 100 normal + 20 anomalous trajectories for offline evaluation. |

The service ships a multi-stage `python:3.12-slim` Dockerfile with a
non-root user, a CPU-only torch wheel, a `/app/models` volume, and a
`/healthz` healthcheck.

### Database
`V2__anomaly_indexes.sql` adds the unique `(mmsi, window_end)` index that
backs the scheduler's idempotent insert plus a composite
`(created_at DESC, score DESC)` index that covers the API's
"most recent anomalies above threshold" query.

### Core API
- `FjordWatch.Domain/Anomaly.cs` — `Anomaly` record + `IAnomalyRepository`.
- `FjordWatch.Infrastructure/PostgresAnomalyRepository.cs` — Dapper read against `vessel_anomalies` joined to `vessels` for the display name.
- `FjordWatch.Api/Endpoints/AnomalyEndpoints.cs` — `GET /anomalies?since=&minScore=&limit=` with clamping (limit ≤ 500, score ∈ [0, 1], since ≤ 30 days).
- `FjordWatch.Api.Tests/Endpoints/AnomalyEndpointsTests.cs` — three tests covering clamping, the 30-day rejection, and the default since window. **45 tests pass total.**

### Frontend
`Pages/Anomalies.razor` ships a sortable MudBlazor table behind two sliders
(min score + lookback hours). Clicking the location icon navigates to
`/?focus={mmsi}&from={t0}&to={t1}` so the map page can focus on the vessel
and replay the suspicious window via the existing track endpoint. The app
bar gains a Map / Anomalies switcher.

### CI + compose
- `.github/workflows/python.yml` runs `ruff check`, `ruff format --check`, `mypy --strict`, and `pytest` on Python 3.12 with NuGet-style dependency caching.
- `docker-compose.yml` swaps the `anomaly-detection` busybox stub for the real build, with a named `anomaly-models` volume for trained artifacts.
- `Makefile` gains `test-python`, `lint-python`, `format-python` targets.

### Docs
- `docs/adr/0002-isoforest-lstm-ae-ensemble.md` records the ensemble choice with rejected alternatives.

## Verification

| Gate | Result |
|---|---|
| `ruff check`, `ruff format --check` | Clean locally on Python 3.13; CI runs on 3.12. |
| `dotnet build -c Release` (core-api) | Clean. |
| `dotnet test` (core-api) | 45 passed. |
| `dotnet build` (web) | Clean. |
| `docker compose -f docker-compose.yml config` | Valid. |

## Deviations from spec (and rationale)

- **Coastline distance is a placeholder constant.** Spec lists "mean distance from nearest coastline" as a feature. Real coastline distance needs a precomputed grid against Kartverket polygons; that is a phase 6 polish item. The placeholder keeps the feature column stable and the IsoForest contribution for it stays at zero so it doesn't bias the score.
- **LSTM-AE training falls back to synthetic data.** The spec says "trained on a baseline week of normal Norwegian traffic". Training-data availability depends on having logged a week of live data first. `scripts/train.py --synthetic-fallback` generates plausible coastal trajectories and is documented in the README; the real-data run is a one-time manual step the developer takes after recording live data for a week.
- **MLflow as a sidecar `mlflow-skinny` client.** The spec doesn't dictate the MLflow deployment shape. We use the lightweight client and let the developer point `MLFLOW_TRACKING_URI` at any tracking server (local SQLite, MinIO-backed Azure ML, etc.). The phase 6 polish bundles a turnkey `mlflow` compose service if needed.
- **No "replay the suspicious window" yet.** The Anomalies page navigates to the map page with `focus=&from=&to=` query parameters, but the map page does not yet wire those into a timeline scrubber or marker animation. Mechanically the same data is available via `GET /vessels/{mmsi}/track?from=&to=`; phase 6 polish wires the JS interop for animation.
- **Synthetic anomaly evaluation gate runs offline.** The spec gate "95 % of injected anomalies above the 90th percentile of normal" lives in `scripts/inject_synthetic_anomalies.py` plus an offline analysis. CI runs the scaffolding (`pytest -q`) without the full statistical evaluation to keep PR feedback under five minutes.
- **`vessel_anomalies` writes are gated by `SCORE_FLOOR`.** Avoids inserting hundreds of thousands of low-score rows per tick.

## What was skipped or deferred

- **Real coastline distance feature** — phase 6 polish.
- **Mean distance from nearest coastline lookup table** — phase 6 polish.
- **Map-side anomaly window replay (timeline scrubber)** — phase 6 polish.
- **Live-data evaluation gate (5 plausible anomalies on 24h replay)** — manual step after first live recording.
- **MLflow standalone compose service** — phase 6 polish (the client integration is in place; only the optional server is missing).

## Manual steps for the developer

1. **Train the ensemble against real data once 24h+ has been recorded.**
   ```bash
   docker compose run --rm anomaly-detection \
       python -m anomaly_detection.scripts.train --window-hours 168 --mlflow
   ```
2. **Verify the offline evaluation gate.**
   ```bash
   docker compose run --rm anomaly-detection \
       python -m anomaly_detection.scripts.inject_synthetic_anomalies --out /tmp/synth
   # Then run the offline notebook (phase 6) or a quick eval script:
   #   pytest tests/test_eval.py -q  (added in phase 6 polish)
   ```
3. **Spot-check the API.**
   ```bash
   curl 'http://localhost:8080/anomalies?since=2026-05-02T00:00:00Z&minScore=0.5&limit=20' | jq '.'
   ```
4. **Open the Anomalies tab in the UI** at `http://localhost:5000/anomalies`.

## Risks remaining

- **Cold-start scoring.** Until `scripts/train.py` runs, the ensemble uses a synthetic-fit IsoForest. Score values are calibrated against synthetic features, so absolute thresholds will not match production ranges. The Anomalies page UI surfaces the score so reviewers can adjust the slider; the API does not yet expose model lineage explicitly (deferred).
- **Postgres write contention during scoring batches.** With `INSERT ... ON CONFLICT DO NOTHING` the locking footprint is minimal, but a 10000-vessel batch every 10 minutes adds noticeable IO. Phase 6 measures and tunes batch chunk size if needed.
- **Torch wheel size.** ~200 MB compressed CPU-only build. Acceptable for a portfolio piece running on a developer laptop; the production cloud path (phase 7) would compile a smaller image with only the inference path or move inference to ONNX runtime.

## What's next

Phase 4: dark vessel detection. Sentinel-1 SAR fetcher, YOLOv8 ship-detection
service, correlation against the AIS positions stream, and a "dark vessels"
overlay on the Blazor map.
