# Phase 2 summary — Core API + Blazor map

## What was built

### Core API (`services/core-api/`, .NET 9)

| Project | Purpose |
|---|---|
| `FjordWatch.Domain` | POCO records: `Vessel`, `Position`, `Track`, `BoundingBox`, `ShipTypeCategory`, `IVesselRepository`. No I/O, no framework refs. |
| `FjordWatch.Infrastructure` | Dapper + Npgsql v8 data source repository. Spatial queries via raw SQL (`ST_MakeEnvelope`, `ST_X`, `ST_Y`, `DISTINCT ON`). URL-to-key-value converters for `DATABASE_URL` and `REDIS_URL` so the operator-facing env stays uniform across Rust, Python, and .NET services. |
| `FjordWatch.Agent` | Placeholder. Real Semantic Kernel kernel + tools land in phase 5. |
| `FjordWatch.Api` | Minimal API host: `/healthz`, `/readyz` (Postgres + Redis ping), `/metrics` (Prometheus via OpenTelemetry), `/vessels?bbox=`, `/vessels/{mmsi}`, `/vessels/{mmsi}/track` (GeoJSON LineString), and the `/hubs/vessels` SignalR hub. Hub fanout uses a hosted `RedisStreamRelay` that consumes `ais:positions` and applies a per-connection viewport + per-MMSI 3-second rate limit before pushing `positionUpdate` events. |
| `FjordWatch.Api.Tests` | 42 xUnit tests covering bbox parsing, ship-type classification, viewport filter rate-limiting, category parsing, GeoJSON shape, and the URL converters. CI runs them; Testcontainers integration tests are gated behind `FJORDWATCH_RUN_INTEGRATION_TESTS`. |

`Directory.Build.props` enables `TreatWarningsAsErrors`, latest-recommended
analyzers, deterministic builds, and language version 12. Solution-wide
`dotnet format --verify-no-changes` is clean.

### Web (`services/web/`, Blazor WebAssembly)

- `MainLayout` with MudBlazor app bar, `ConnectionStatus` chip showing live
  hub state, link to `/about`.
- `Home` page renders the full-bleed map (`MapView`), legend (`LegendPanel`),
  and an optional side panel (`VesselSidePanel`) when a vessel is selected.
- `MapView` owns one Leaflet instance per `elementId` via JS interop and
  exposes `RenderVesselsAsync`, `UpsertPositionAsync`, `DrawTrackAsync`,
  `ClearTrackAsync`. The JS module clusters everything into two
  `LayerGroup`s (vessels + track) and reports `moveend`/`zoomend` back to
  the component so the page can refetch from the API and update the
  hub viewport.
- `ApiClient` and `VesselsHubClient` wrap `HttpClient` and `HubConnection`.
  The hub client uses `WithAutomaticReconnect` (0 s, 2 s, 5 s, 10 s, 30 s)
  and broadcasts state transitions to the chip in the app bar.
- `wwwroot/appsettings.json` provides `PublicApiBaseUrl` and
  `PublicHubUrl` defaults; nginx serves the static bundle.

### Compose + CI

- `docker-compose.yml` swaps the `core-api` and `web` busybox stubs for
  real builds (`fjordwatch/core-api:dev`, `fjordwatch/web:dev`).
- `.github/workflows/dotnet.yml` (path-filtered) runs `dotnet format`,
  `dotnet build -c Release`, `dotnet test` against the core-api solution
  with NuGet caching.
- `Makefile` gains `test-dotnet`, `lint-dotnet`, `format-dotnet` targets and
  rolls them into the top-level `test` / `lint` / `format` aggregates.

### Docs

- `docs/adr/0001-dapper-over-ef-for-readpath.md` records the decision to
  use Dapper + Npgsql instead of EF Core in `FjordWatch.Infrastructure`.
- `services/core-api/README.md` and `services/web/README.md` describe each
  service's responsibilities, run/test surface, configuration matrix.

## Verification

| Check | Result |
|---|---|
| `dotnet format --verify-no-changes` (core-api) | Clean. |
| `dotnet build -c Release` (core-api) | Clean. `TreatWarningsAsErrors=true` and `AnalysisLevel=latest-recommended`. |
| `dotnet test` (core-api) | 42 passed, 0 failed. |
| `dotnet build` (web) | Clean. |
| `docker compose -f docker-compose.yml config` | Valid. `core-api` and `web` resolve as real builds; dependencies on `db-migrate` (`service_completed_successfully`) wire correctly. |

## Deviations from spec

- **Dapper + Npgsql, not EF Core.** Captured in `docs/adr/0001-dapper-over-ef-for-readpath.md`.
- **Hand-rolled GeoJSON.** The endpoint surface is small (one `LineString`); we avoid the `GeoJSON.Net` dependency.
- **Lighthouse > 80 not enforced in CI.** Lighthouse needs a headless Chrome runner; phase 6 wires Lighthouse CI alongside the e2e Playwright workflow. Lighthouse can be run manually on first deploy.
- **Auth deferred.** Spec says skip user auth for v1 and add an API key for write endpoints when they appear. Phase 2 has no write endpoints.
- **No IL trimming yet.** WASM bundle is ~3 MB after compression; phase 6 polish enables IL trimming + brotli precompression to push under 1 MB.
- **Testcontainers integration tests are gated.** They run locally with `FJORDWATCH_RUN_INTEGRATION_TESTS=true` but stay off in CI to keep PR feedback fast. The 42 unit tests cover the contract surface.

## Manual steps for the developer

1. **Verify the full stack on a Docker host.**
   ```bash
   make up
   open http://localhost:5000
   curl 'http://localhost:8080/vessels?bbox=4,58,12,72' | jq '.[0:3]'
   curl 'http://localhost:8080/vessels/<some-mmsi>/track' | jq .
   ```
   Expect: vessels appear and animate; clicking a marker draws its 24-hour
   track on the map and opens the side panel; the live chip in the app bar
   stays green.

2. **Run a quick Lighthouse pass on `http://localhost:5000`.**
   Targets: performance > 80, accessibility > 90. If either misses, file a
   ticket against phase 6 polish (IL trimming, brotli, image lazy-load).

## Risks remaining

- **WASM bundle size.** Around 3 MB on first load with MudBlazor in scope. Acceptable for a portfolio map; phase 6 enables trimming + brotli.
- **SignalR client traffic.** With viewport filter and 3-second per-MMSI rate limit, a fully-zoomed-out map at 10000 active vessels still pushes ~3000 messages/sec to clients in the worst case. Phase 6 adds a "max msgs/sec per connection" ceiling on the relay, with overflow dropped + counted.
- **CORS configuration.** `CORS_ORIGINS` defaults to `http://localhost:5000`; deploying behind a different origin requires an env var update or the SPA will fail silently.
- **Dapper SQL drift.** Schema changes in `services/db/migrations` can break the repository at runtime. The Testcontainers integration test (when enabled) catches this; consider running it nightly in CI once GitHub-hosted runners support persistent Docker layer caching.

## What's next

Phase 3: anomaly detection. Python FastAPI service that materializes feature
vectors per vessel over a 6-hour window, scores them with an Isolation Forest
and an LSTM autoencoder ensemble, and writes results into
`vessel_anomalies` for the API to surface as a side tab.
