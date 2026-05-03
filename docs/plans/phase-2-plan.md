# Phase 2 plan — Core API + Blazor map

## Goal
A user runs `make up`, opens the web app, and sees Norwegian vessels move on a Leaflet map in real time. Clicking a vessel renders its 24-hour track. The .NET 9 core API serves vessel queries and proxies the Redis Stream to a SignalR hub. CI gates merge with green dotnet build, format check, and xUnit tests.

## Files to create

### .NET 9 core API solution (`services/core-api/`)
1. `FjordWatch.sln` and `Directory.Build.props` (centralized treat-warnings-as-errors, nullable enabled, language version 12).
2. `FjordWatch.Domain/` — pure domain types: `Vessel`, `Position`, `Track`, `BoundingBox`, `ShipTypeCategory`. No EF, no I/O.
3. `FjordWatch.Infrastructure/` — `IVesselRepository`, Postgres + Npgsql + Dapper implementation (Dapper is lighter than EF Core for read-heavy spatial queries and avoids EF's spatial-type fragility). PostGIS calls via raw SQL with parameterized bbox.
4. `FjordWatch.Api/` — Minimal API host. Endpoints: `GET /vessels?bbox=&types=`, `GET /vessels/{mmsi}`, `GET /vessels/{mmsi}/track?from=&to=`, `GET /healthz`, `GET /readyz`, `GET /metrics`. Response shapes match GeoJSON for tracks, lean DTOs for the rest. Includes the `VesselsHub` SignalR hub at `/hubs/vessels` with a hosted `RedisStreamRelay` that consumes `ais:positions` via a consumer group and fans out to clients filtered by their declared viewport.
5. `FjordWatch.Agent/` — placeholder project with a single `IAgent` interface. Implementation lands in phase 5.
6. `FjordWatch.Api.Tests/` — xUnit + `WebApplicationFactory` for end-to-end API tests (with Postgres + Redis Testcontainers gated behind a feature flag so CI can run a fast subset). Pure-unit tests cover bbox parsing, GeoJSON serialization, and the hub viewport filter.
7. `services/core-api/Dockerfile` — multi-stage `mcr.microsoft.com/dotnet/sdk:9.0` builder, `aspnet:9.0` runtime, non-root user, healthcheck against `/healthz`.

### Web app (`services/web/`)
8. `FjordWatch.Web/` — Blazor WebAssembly project with `MudBlazor` for components, `Microsoft.AspNetCore.SignalR.Client`, and a JS interop layer for Leaflet. Pages: `/` (map), `/about`. Components: `MapView`, `VesselSidePanel`, `LegendPanel`, `ConnectionStatus`. JS: `wwwroot/js/leaflet-interop.js` wraps `L.map`, `L.tileLayer` (OpenStreetMap base + OpenSeaMap overlay), `L.circleMarker`, viewport `moveend` events.
9. `services/web/Dockerfile` — builds with `dotnet publish`, serves via `nginx:alpine` (small + fast for a WASM bundle, with gzip + brotli).

### CI + compose
10. `.github/workflows/dotnet.yml` — path-filtered (`services/core-api/**`, `services/web/**`, the workflow itself). Steps: `dotnet format --verify-no-changes`, `dotnet build -c Release`, `dotnet test --logger trx`. Uses `actions/setup-dotnet@v4` and the `dotnet` cache plugin.
11. `docker-compose.yml` — replace `core-api` and `web` busybox stubs with real builds. Wire env vars (`DATABASE_URL`, `REDIS_URL`, `CORE_API_PORT`, `PUBLIC_API_BASE_URL`, `PUBLIC_HUB_URL`).

### Docs
12. `docs/adr/0001-dapper-over-ef-for-readpath.md` — record the Dapper+Npgsql vs EF Core decision now that we're making it.
13. `services/core-api/README.md` — solution layout, run, test, ports, observability surface.
14. `services/web/README.md` — what it does, how to dev (`dotnet watch run` against a running stack), build.

## Deviations from spec

- **Dapper + Npgsql, not EF Core, for the read path.** EF Core's PostGIS support via `NetTopologySuite` is workable but heavyweight for our endpoint set (three read queries, one streaming bbox query, no aggregates that benefit from LINQ). Dapper keeps queries hand-written, which is the right shape for a portfolio piece showing senior backend judgement, and avoids the multi-second WASM build penalty when types leak across project boundaries. Captured in `docs/adr/0001`.
- **MudBlazor instead of plain Blazor.** Spec lists MudBlazor; documenting here that we consciously kept it (rather than switching to FluentUI Blazor) because of MudBlazor's better drawer + side-panel ergonomics for the side-panel UX.
- **Nginx for the WASM bundle.** Spec doesn't dictate; nginx with brotli precompression is the standard production pattern and keeps the dev image small. The Blazor server-side hosting model would couple the web app to .NET, which we don't want for a WASM SPA.
- **No `aspnet:9.0` chiseled image yet.** The chiseled-Ubuntu runtime image is smaller but doesn't include the curl binary used by `HEALTHCHECK`. Phase 6 polish swaps this if we add a /healthz wget-friendly probe.
- **Lighthouse score check is manual in this phase.** The spec calls for "> 80 Lighthouse score on the map page". CI cannot run Lighthouse without a headless Chrome runner, which adds 4–6 minutes to PR feedback. Manual check on first run is acceptable; phase 6 wires Lighthouse CI alongside the e2e Playwright workflow.
- **API auth deferred to phase 5+ when the agent appears.** Spec section says "skip user auth for v1; add a single API key for write endpoints when they appear". Phase 2 has no write endpoints, so no auth is added.

## Verification

- `dotnet format --verify-no-changes` clean.
- `dotnet build -c Release` clean (treat warnings as errors enabled).
- `dotnet test` green: bbox parsing, hub viewport filter, GeoJSON serializer, plus integration test that hits a Testcontainers Postgres + Redis under a `Integration` trait gate.
- `docker compose up core-api web` reaches healthy.
- `curl localhost:8080/vessels?bbox=4,58,12,72` returns a JSON array.
- `curl localhost:8080/vessels/{mmsi}/track?from=...&to=...` returns valid GeoJSON LineString.
- Open `http://localhost:5000`, see vessels move; click one, see its 24-hour track render.
- `make up` brings the whole stack to healthy.
- CI: both `compose-validate`, `rust`, and the new `dotnet` workflow green.

## Risks

- **Testcontainers in CI.** GitHub-hosted runners support Docker but image pulls add 30–60 s. We gate integration tests behind an env var so unit tests stay fast.
- **WASM bundle size.** MudBlazor pulls in CSS + a fair JS surface; expect ~3 MB initial load. Acceptable for a portfolio map; phase 6 enables IL trimming and brotli to push under 1 MB.
- **SignalR + viewport filtering correctness.** The hub must not leak vessels outside a client's bbox or trigger thrashing on small zoom changes. We add a unit test on the filter and an integration test that subscribes from two synthetic clients and asserts non-overlap.
- **Live SignalR + Redis backpressure.** If Redis Stream growth outpaces the relay's read rate, clients fall behind silently. We bound the consumer group's pending list and alert on length in the metrics endpoint.
