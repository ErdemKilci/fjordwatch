# Phase 1 summary — AIS ingestion

## What was built

| Artefact | Purpose |
|---|---|
| `services/db/migrations/V1__init.sql` | Initial PostGIS schema. `vessels`, `positions`, `vessel_anomalies` (stub), `sar_detections` (stub), supporting indexes, the `vessel_tracks` materialized view, and the `touch_vessel_last_seen` trigger. |
| `services/ais-ingestion/Cargo.toml` | Rust workspace package. Pinned to `tokio` 1.42, `sqlx` 0.8 (runtime queries, no compile-time DB), `redis` 0.27, `axum` 0.7, `ais` 0.10, plus `tracing`, `clap`, `metrics`. Strict lint config: `clippy::all` denied, `pedantic` and `nursery` warned, `unsafe_code` forbidden. |
| `services/ais-ingestion/src/{main,lib,config,error,source,decoder,store,stream,telemetry}.rs` | The service. Pipeline: `source` (TCP live or replay file) -> `decoder` (`ais` crate, normalized into `DecodedMessage`) -> `stream` (Redis xadd) + `store` (Postgres batched upserts in a transaction). Each stage is a tokio task connected by bounded mpsc channels for back pressure. |
| `services/ais-ingestion/tests/replay_fixture.rs` | End-to-end integration test that runs the bundled NMEA fixture through the parser without external services. |
| `services/ais-ingestion/tests/fixtures/sample.nmea` | Public-source NMEA sentences covering message types 1/3/5/18/21/24. Small enough to keep in Git directly. |
| `services/ais-ingestion/Dockerfile` | Multi-stage build with cached dependency layer, non-root user (`app:10001`), `HEALTHCHECK` against `/healthz`. |
| `services/ais-ingestion/README.md` | What/why/run/test, configuration matrix, observability surface. |
| `docker-compose.yml` | Real `ais-ingestion` service replaces the busybox stub. New one-shot `db-migrate` Flyway service applies migrations and gates dependent services via `service_completed_successfully`. |
| `Makefile` | `test`, `test-rust`, `lint`, `lint-rust`, `format`, `format-rust`, `migrate`, `ais-replay` targets wired up. |
| `.env.example` + `.env` | Added `AIS_REPLAY_DELAY_MS`, `AIS_METRICS_PORT`, plus a comment on the `/fixtures` mount path. |
| `.github/workflows/rust.yml` | Path-filtered CI (push + PR) running `cargo fmt --check`, `cargo clippy -D warnings`, `cargo test --workspace --all-targets`, with `Swatinem/rust-cache` for cargo cache. |

## Verification

| Check | Result |
|---|---|
| `cargo fmt --all -- --check` | Clean. |
| `cargo clippy --workspace --all-targets -- -D warnings` | Clean. (Strict lints: `pedantic` and `nursery` enabled in `Cargo.toml`.) |
| `cargo test --workspace --all-targets` | 8 passed, 0 failed (7 unit + 1 integration). |
| `docker compose -f docker-compose.yml config` | Valid. The `db-migrate` and `ais-ingestion` services resolve, dependencies wire up, and the build context for `ais-ingestion` points at `services/ais-ingestion`. |

## Deviations from spec (and rationale)

- **Smaller NMEA fixture instead of 24 hours.** The spec says "Record 24 hours of live data into a fixture file checked into Git LFS or stored in MinIO seed data." Recording 24 hours requires a sustained TCP connection to Kystverket from a developer machine, which is a real-world step the developer can perform once. The phase 1 fixture covers parser correctness across types 1/3/5/18/21/24, and a `make ais-record` target will be added in phase 6 polish for the developer to capture their own multi-hour sample into MinIO.
- **`sqlx` runtime queries, no compile-time macros.** Keeps CI builds free of a live database. The query shapes here (one upsert into `vessels`, one insert into `positions`) are simple enough that the loss of compile-time check is acceptable; phase 2 (.NET API) does not depend on `sqlx` at all.
- **`ais::messages::types::ShipType` extracted via `Debug` round-trip.** The upstream crate exposes `ShipType` as an opaque enum without a stable numeric accessor on its public API. Rather than vendor a hand-rolled mapping that drifts, the normalizer extracts the wire code from the `Debug` representation. This is local to one helper (`ship_type_to_u8` in `decoder.rs`) and is documented in a comment.
- **Rate-of-turn deferred.** The `ais` crate keeps its `RateOfTurn` type in a private module, so the position struct surfaces `None` for now. Phase 3 anomaly detection computes its own ROT from successive headings; nothing downstream is blocked.
- **Migrations via Flyway one-shot container.** A `restart: "no"` Flyway container that exits after running migrations is the same pattern as an init container for compose's purposes and keeps the dependency graph declarative (`condition: service_completed_successfully`).

## What was skipped or deferred

- **24-hour recorded fixture.** Deferred to phase 6 (`make ais-record`).
- **Real Postgres-backed integration test for the writer.** Could be added by spinning a temporary `postgis/postgis` container; left as `#[ignore]`-style follow-up since CI does not need a database for phase 1's contract.
- **`metrics-exporter-prometheus` `tokio_runtime_metrics` integration.** Counter set is sufficient for now. Phase 6 wires up runtime metrics + Grafana dashboards.

## Manual steps for the developer

1. **Verify on the live feed (one-time, locally).** With Docker Desktop running:
   ```bash
   make up
   docker compose logs -f ais-ingestion
   psql "$DATABASE_URL" -c "SELECT COUNT(*) FROM positions;"
   ```
   After ~60 seconds, the count should be > 0; after 5 minutes,
   `SELECT COUNT(DISTINCT mmsi) FROM positions;` should clear 100 (Norwegian
   coastal traffic typically delivers several hundred unique MMSIs in the
   first few minutes).

2. **Replay the bundled fixture without the live socket.**
   ```bash
   AIS_REPLAY_FILE=/fixtures/sample.nmea make ais-replay
   docker compose logs -f ais-ingestion
   ```

3. **Start a 24-hour recording into MinIO** (deferred to phase 6 polish; left
   as a single `make ais-record` invocation that writes to `s3://fixtures/`).

## Risks remaining

- **Live socket flakiness.** The Kystverket TCP feed occasionally drops connections. The reconnect loop with exponential backoff handles this, but a sustained outage past `AIS_RECONNECT_MAX_BACKOFF_MS` will only manifest as zero `ais_lines_in_total` growth. Phase 6 adds a Grafana alert on stalled lines-in.
- **Postgres write back pressure.** Channel capacities (2048 raw, 4096 decoded) are sized for steady state; a sustained Postgres outage will block the source. Acceptable for v1 (correctness over throughput); a dead-letter to disk would be a phase 6 hardening item.
- **`ais` crate API drift.** The `Debug` round-trip for `ShipType` and the deferred `RateOfTurn` field both depend on upstream internals. CI will catch breakage on every push; the helpers are localized to `decoder.rs` for fast repair.

## What's next

Phase 2: .NET 8 core API + SignalR vessel hub. Reads from `positions` and the
`ais:positions` Redis Stream consumer group, exposes REST endpoints for
vessel queries, and pushes live deltas to the Blazor map via SignalR.
