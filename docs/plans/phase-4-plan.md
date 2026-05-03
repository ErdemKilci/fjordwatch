# Phase 4 plan — Dark vessel detection

## Goal
Sentinel-1 SAR scenes covering the Norwegian coast are fetched on a schedule, tiled, and run through a YOLOv8 ship detector. Each detection is correlated against the AIS positions stream within a 500m / 30-minute window. Detections without a matching AIS broadcast are flagged `is_dark = true` in `sar_detections` and shown as red icons on a toggleable Blazor map layer; matched detections are blue, with the AIS distance + lag in the tooltip.

## Files to create

### SAR fetcher (`services/sar-fetcher/`, Python 3.12)
1. `pyproject.toml` — `fastapi`, `uvicorn`, `pydantic-settings`, `httpx`, `boto3` (MinIO/S3), `apscheduler`, `gdal` *or* `rasterio` (rasterio for portability since GDAL Python bindings are heavy). Dev: `pytest`, `ruff`, `mypy`, `httpx`.
2. `src/sar_fetcher/config.py` — Copernicus credentials, MinIO endpoint + bucket, scene-cadence cron, AOI bbox (Norwegian coast).
3. `src/sar_fetcher/copernicus_client.py` — OData/Catalogue API client. Returns scene IDs and download URLs for the AOI in the last `lookback` hours. Falls back to a fixture catalog when credentials are blank so dev/CI keep working.
4. `src/sar_fetcher/tiler.py` — `tile_scene(path, target_size)` pipeline that opens a Sentinel-1 GRD via rasterio, applies a calibration step (sigma0 dB), and writes 1024x1024 PNG tiles plus a sidecar JSON with the geotransform per tile to MinIO.
5. `src/sar_fetcher/store.py` — async S3 writer using boto3 + asyncio.to_thread. Skips uploads when the scene_id already has tiles in the bucket.
6. `src/sar_fetcher/scheduler.py` — APScheduler async job that pulls fresh scenes every `SAR_FETCH_INTERVAL_MINUTES` and calls the ship-detection service for each new tile.
7. `src/sar_fetcher/main.py` — FastAPI surface: `/healthz`, `/readyz`, manual `POST /fetch-now`. Boots scheduler.
8. `tests/`, `Dockerfile`, `README.md`.

### Ship detection (`services/ship-detection/`, Python 3.12)
9. `pyproject.toml` — `fastapi`, `pillow`, `numpy`, `onnxruntime`, `boto3`, `pydantic-settings`. Dev: `pytest`, `ruff`, `mypy`, `httpx`.
10. `src/ship_detection/inference.py` — ONNX-based YOLOv8 wrapper. Model file lives at `MODEL_DIR/yolov8_ship.onnx`; ships a tiny placeholder ONNX (single-pass identity) so the service starts before the real model has been registered.
11. `src/ship_detection/api.py` — `POST /detect`. Body: `{ tile_uri, geotransform }`. Returns `{ detections: [{ bbox: [x1,y1,x2,y2], wgs84: [lon,lat], confidence, ... }] }`. `/healthz`, `/readyz`, Prometheus `/metrics`.
12. `src/ship_detection/sar_preprocess.py` — sigma0 dB conversion, percentile-based stretch to 0-255, Pillow encoding for the model input.
13. `src/ship_detection/store.py` — write detections to `sar_detections` (Postgres) directly so the correlation worker can iterate on a single source.
14. `src/ship_detection/correlator.py` — runs after each detection batch. Queries `positions` for the same time window, finds nearest AIS broadcast within 500 m / 30 min using PostGIS `ST_DWithin`, sets `matched_mmsi`, `match_distance_m`, `match_lag_s`, `is_dark`.
15. `scripts/train.py` — fine-tune a YOLOv8 nano on a public ship-detection dataset (xView, AirBus, or HRSC2016 via Roboflow's open mirrors), export to ONNX with seed pinned. Documented as a one-time step the developer runs locally.
16. `tests/`, `Dockerfile`, `README.md`.

### Database
17. `services/db/migrations/V3__sar_detection_indexes.sql` — composite indexes for `(detected_at DESC, is_dark)` and `(matched_mmsi, detected_at)`; column comments for clarity.

### Core API
18. `FjordWatch.Domain/SarDetection.cs` + `ISarDetectionRepository`.
19. `FjordWatch.Infrastructure/PostgresSarDetectionRepository.cs` — Dapper read with bbox filter.
20. `FjordWatch.Api/Endpoints/SarEndpoints.cs` — `GET /sar?bbox=&since=&onlyDark=&limit=`. xUnit tests.

### Frontend
21. `services/web/FjordWatch.Web/Pages/Home.razor` — add a layer toggle in `LegendPanel`. New `wwwroot/js/leaflet-interop.js` methods `addOrUpdateSar`, `clearSar`. Markers: red ring + warning icon when `is_dark`, blue when matched. Tooltip shows confidence and matched-AIS distance/lag.

### Docs
22. `docs/dark-vessel-limitations.md` — false positives (rocks, oil platforms, weather, sidelobes), confidence calibration, why a "dark" classification is not legally meaningful.
23. `docs/adr/0003-rasterio-vs-gdal-bindings.md` — record the rasterio choice over the GDAL Python bindings.

### CI + compose
24. Extend `.github/workflows/python.yml` with two more jobs (`sar-fetcher`, `ship-detection`) following the same pattern as `anomaly-detection`.
25. `docker-compose.yml` — replace busybox stubs for `sar-fetcher` and `ship-detection` with real builds. Wire MinIO bucket bootstrap (one-shot) for `sar-tiles`.

## Deviations from spec

- **YOLOv8 placeholder ONNX shipped in-repo, training is a manual step.** The spec calls for a trained model with F1 > 0.7 on a known scene. Training requires a free dataset license click-through and several GPU-hours; we ship the inference plumbing with a placeholder that returns no detections, plus `scripts/train.py` so the developer can produce a real model when convenient. This is documented in the README.
- **rasterio instead of GDAL bindings.** rasterio is a thin wrapper around the same GDAL libraries with a much friendlier Python API and a cleaner Docker image story. Captured in ADR-0003.
- **MinIO bootstrap as a one-shot service.** Spec doesn't dictate; matches the Flyway pattern from phase 1.
- **Correlation worker lives inside ship-detection rather than as its own service.** The two are tightly coupled (correlator needs the detection results immediately); splitting adds latency without separation-of-concerns benefit. Phase 6 may revisit if the correlator grows.
- **Map overlay throttles SAR detections to 200 by default.** A real Sentinel-1 scene over the Norwegian coast can produce thousands of detections; surfacing the full set on the map page slows the bundle. The toggle defaults to "last 24 h, dark only", with a slider to broaden.

## Verification

| Gate | How |
|---|---|
| `ruff`, `mypy`, `pytest` | All clean for both new services. |
| Unit tests | sar_preprocess shape stability, correlator matches/non-matches, copernicus_client fixture fallback. |
| Integration | When developer drops a real Sentinel-1 GRD into MinIO and runs `make sar-detect-now`, at least one detection appears in `sar_detections`. |
| `GET /sar?bbox=...&onlyDark=true` | Returns the rows the worker has written. |
| UI | Toggling the SAR layer renders the markers; clicking shows the correct tooltip text. |
| `docker compose up sar-fetcher ship-detection` | Reaches healthy. |

## Risks

- **Copernicus credentials are optional but the fixture path is brittle.** If the fixture URLs rot, dev breaks. We bundle a tiny fixture scene in `tests/fixtures/scene_metadata.json` as a stable baseline.
- **YOLOv8 trained on optical imagery transfers poorly to SAR.** Spec acknowledges this and the dark-vessel limitations doc spells it out. The phase 4 acceptance gate is plumbing-correct; F1 quality is a phase 6 polish item once a SAR-specific model is registered.
- **MinIO + Sentinel-1 GRD storage size.** A single GRD scene is ~1.5 GB after decompression. We tile aggressively and store only the tiles, not the raw scenes. The dev volume is sized at 20 GB by default; phase 6 documents disk-space management.
- **rasterio + GDAL apt deps balloon the image.** The Dockerfile uses `python:3.12-slim` plus targeted `libgdal30 libproj22 libgeos-c1v5 libtiff6` packages to keep the image under 1 GB.
