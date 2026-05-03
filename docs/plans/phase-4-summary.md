# Phase 4 summary — Dark vessel detection

## What was built

### SAR fetcher (`services/sar-fetcher/`)
- Copernicus catalog client with a fixture-fallback path so dev/CI work without credentials.
- rasterio-based tiler that converts Sentinel-1 GRD intensity into 1024x1024 PNG tiles plus a sidecar JSON (geotransform, bounds, tile size).
- boto3 MinIO writer that skips already-uploaded scenes and uploads tiles + sidecars to `sar-tiles/{scene_id}/`.
- APScheduler-driven `FetchJob` runs every `SAR_FETCH_INTERVAL_MINUTES` and notifies `ship-detection` over HTTP when a new scene's tiles are ready.
- FastAPI surface: `/healthz`, `/readyz`, `POST /fetch-now`, Prometheus `/metrics`.

### Ship detection (`services/ship-detection/`)
- ONNX-based YOLOv8 wrapper. Ships a placeholder mode that returns zero detections when the artifact is missing, so the service is callable before training has run.
- `POST /detect` accepts the sar-fetcher payload, fetches each tile + sidecar from MinIO, runs inference, projects pixel bboxes to WGS84 via the tile sidecar, persists rows into `sar_detections`, and runs the correlator in the same call.
- Correlator uses PostGIS `ST_DWithin` to find AIS positions within `CORRELATION_RADIUS_M` and `CORRELATION_WINDOW_S` (defaults 500 m / 30 min). Updates rows in place with `matched_mmsi`, `match_distance_m`, `match_lag_s`, and `is_dark`.

### Database
- `V3__sar_detection_indexes.sql` adds composite indexes for the API's read patterns and column comments documenting `is_dark`, `match_distance_m`, `match_lag_s` semantics.

### Core API
- `FjordWatch.Domain/SarDetection.cs` + `ISarDetectionRepository`.
- `FjordWatch.Infrastructure/PostgresSarDetectionRepository.cs` Dapper read with bbox + since + onlyDark filters.
- `FjordWatch.Api/Endpoints/SarEndpoints.cs` exposes `GET /sar?bbox=&since=&onlyDark=&limit=` with the same clamping pattern as `/anomalies`.
- 4 new xUnit tests; **49 tests pass total**.

### Frontend
- New `wwwroot/js/leaflet-interop.js` methods: `addOrUpdateSar`, `clearSar`, `toggleSarLayer`. Markers are red when `is_dark`, blue when matched, with tooltips showing confidence (dark) or matched MMSI + distance + lag (matched).
- `MapView.razor` exposes `RenderSarAsync`, `ClearSarAsync`, `ToggleSarLayerAsync`.
- `Home.razor` adds a small floating panel in the top-left with a "SAR overlay" toggle and a nested "Dark only" sub-toggle. The page refetches SAR detections on each viewport change while the layer is enabled.

### CI + compose
- `python.yml` gains two more jobs (`sar-fetcher`, `ship-detection`) following the same pattern as anomaly-detection. `sar-fetcher` job installs the GDAL/GEOS/PROJ dev packages so rasterio builds.
- `docker-compose.yml` swaps the busybox stubs for `ship-detection` and `sar-fetcher` for real builds, with named `ship-models` volume and dependencies on `db-migrate` + `minio`.

### Docs
- `docs/dark-vessel-limitations.md` spells out what "dark" means and does not mean, plus the limitations of this implementation (placeholder model, no platform mask, no SAR-specific augmentation, etc.).
- `docs/adr/0003-rasterio-vs-gdal-bindings.md` records the rasterio choice.

## Verification

| Gate | Result |
|---|---|
| `dotnet build -c Release` (core-api) | Clean. |
| `dotnet test` (core-api) | 49 passed. |
| `dotnet build` (web) | Clean. |
| `docker compose -f docker-compose.yml config` | Valid. |
| `ruff` + `mypy` for both Python services | Clean (verified locally on Python 3.13; CI runs on 3.12). |

## Deviations from spec

- **YOLOv8 placeholder ONNX shipped, training is manual.** Spec requires F1 > 0.7 on a labelled scene; we implement the inference and correlation plumbing and document the training workflow. Real training requires a SAR-specific dataset and several GPU-hours.
- **rasterio over GDAL Python bindings.** ADR-0003.
- **Correlator co-located with `ship-detection`.** The two are tightly coupled; splitting adds latency without separation-of-concerns benefit.
- **MinIO bucket bootstrap is in `sar-fetcher`'s `FetchJob.run_once`.** Avoids adding a separate one-shot service to compose.
- **No oil-platform mask.** Documented in `dark-vessel-limitations.md`; phase 6 polish adds it against Sjøfartsdirektoratet's register.

## What was deferred

- Real YOLOv8 SAR fine-tuning + ONNX export (`scripts/train.py` skeleton ships, the heavy lifting is a phase 6 polish item).
- Oil-platform mask, SAR-specific augmentation, ground-truth-based F1 evaluation.
- Anomaly-window scrubber on the map page (carried over from phase 3 as a polish item).

## Manual steps for the developer

1. **First run with the fixture catalog.** Bring the stack up; the fixture catalog returns a single Lofoten scene but the tiler will not find a real GRD on disk, so no tiles will be written. Expected; this is the dry-run path.
2. **Configure Copernicus credentials** in `.env` to start receiving real scenes.
3. **Train a real model** following the workflow in `services/ship-detection/scripts/train.py` (skeleton). Drop the resulting `yolov8_ship.onnx` into the `ship-models` volume.
4. **Open `http://localhost:5000`**, toggle the SAR overlay, optionally toggle "Dark only".

## Risks remaining

- **rasterio + GDAL apt deps on CI runners.** The `sar-fetcher` job installs `libgdal-dev libproj-dev libgeos-dev`. If those packages move in Ubuntu 24.04, we pin to a known-good major.
- **Placeholder model could mislead reviewers.** The README and `dark-vessel-limitations.md` both say it; the UI legend tooltip clarifies; no further mitigation needed.
- **Storage growth.** Each Sentinel-1 GRD tiles into ~200 PNGs at 1024x1024. A daily cadence at 5 scenes/day fills MinIO at ~3 GB/week. Phase 6 adds a retention policy.

## What's next

Phase 5: LLM agent (Semantic Kernel) with a chat panel and tool use over
`vessels`, `positions`, `vessel_anomalies`, `sar_detections`, plus a small
RAG corpus over public maritime regulations and AIS docs.
