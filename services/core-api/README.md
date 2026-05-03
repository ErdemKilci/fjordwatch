# core-api (.NET 9)

Read-side service for FjordWatch. Exposes REST endpoints over Postgres + a
SignalR hub fed by the Redis Stream that the Rust ingestion service writes to.

## Solution layout

| Project | Purpose |
|---|---|
| `FjordWatch.Domain` | Pure domain types (`Vessel`, `Position`, `Track`, `BoundingBox`, `ShipTypeCategory`, `IVesselRepository`). No I/O, no framework refs. |
| `FjordWatch.Infrastructure` | Dapper + Npgsql repository, URL-to-key-value converters for `DATABASE_URL` and `REDIS_URL`. |
| `FjordWatch.Agent` | Placeholder. Real Semantic Kernel kernel + tools land in phase 5. |
| `FjordWatch.Api` | Minimal API host. Endpoints, SignalR hub, Redis Stream relay, OpenTelemetry metrics. |
| `FjordWatch.Api.Tests` | xUnit + FluentAssertions. Domain and contract tests run in CI; Testcontainers integration tests are gated behind `FJORDWATCH_RUN_INTEGRATION_TESTS=true`. |

## Endpoints

| Method | Path | Returns |
|---|---|---|
| GET | `/healthz` | Always 200 once the process is up. |
| GET | `/readyz` | 200 when Postgres + Redis ping. 503 otherwise. |
| GET | `/metrics` | Prometheus-format scrape (OpenTelemetry exporter). |
| GET | `/vessels?bbox=west,south,east,north&types=cargo,tanker&limit=2000` | Vessels whose latest position is inside the bbox, optionally filtered by coarse `ShipTypeCategory` names. |
| GET | `/vessels/{mmsi}` | Single vessel detail. |
| GET | `/vessels/{mmsi}/track?from=...&to=...` | GeoJSON LineString feature for the requested time window (default last 24 hours, max 48). |

SignalR hub `/hubs/vessels`:

- `SetViewport(west, south, east, north)` declare client viewport.
- `ClearViewport()` revert to no-bbox.
- Server pushes `positionUpdate` events to clients whose viewport contains the
  position; rate-limited to 1 message per MMSI per 3 seconds per connection.

## Run locally

```bash
make up
curl localhost:8080/vessels?bbox=4,58,12,72
```

## Test

```bash
cd services/core-api
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test
```

CI runs the same three commands on every push and PR via
`.github/workflows/dotnet.yml`.

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `DATABASE_URL` | required | Postgres DSN (URL or key-value form). |
| `REDIS_URL` | `redis://redis:6379/0` | Redis URL. |
| `AIS_STREAM` | `ais:positions` | Redis Stream key the relay reads. |
| `CORS_ORIGINS` | `http://localhost:5000` | Comma-separated origins allowed by CORS. |
| `ASPNETCORE_HTTP_PORTS` | `8080` | HTTP listen port. |
