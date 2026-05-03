# Phase 1 plan — AIS ingestion

## Goal
Live (or replayed) AIS data flowing into Postgres and Redis Streams from a Rust service. After 5 minutes of replay, `SELECT COUNT(DISTINCT mmsi) FROM positions;` returns more than 100. `cargo fmt` clean. `cargo clippy -- -D warnings` clean. `cargo test` green.

## Files to create
1. **Database migrations.** `services/db/migrations/V1__init.sql` (vessels, positions, vessel_anomalies stub, sar_detections stub, indexes, materialized view). Flyway runs as a one-shot compose service.
2. **Rust service.** `services/ais-ingestion/` with `Cargo.toml`, `src/main.rs`, `src/config.rs`, `src/nmea.rs`, `src/decoder.rs`, `src/source.rs` (TCP + replay), `src/store.rs`, `src/stream.rs`, `src/telemetry.rs`, `src/error.rs`. Uses `tokio`, `sqlx` (Postgres, runtime queries to avoid build-time DB), `redis`, `axum`, `tracing`, `clap`, `metrics` + `metrics-exporter-prometheus`, and the `ais` crate for AIS payload decoding.
3. **Fixture.** `services/ais-ingestion/tests/fixtures/sample.nmea` with a few hundred hand-curated public AIS sentences covering message types 1/3/5/18/24. Spec calls for 24 hours of recorded live data; that requires sustained access to the live socket which is not feasible in this development environment, so the smaller fixture covers parser correctness while `make ais-record` (added in phase 6 polish) lets the developer record their own 24-hour sample.
4. **Dockerfile.** Multi-stage (cargo-chef-style cached build), non-root user, `HEALTHCHECK` calling `/healthz`.
5. **CI.** `.github/workflows/rust.yml` running `cargo fmt --check`, `cargo clippy -- -D warnings`, `cargo test --workspace`.
6. **Compose wiring.** Replace the busybox `ais-ingestion` stub with the real image, add `db-migrate` service (Flyway image), depend `ais-ingestion` on `db-migrate` and `redis`.
7. **Service README.** `services/ais-ingestion/README.md` with what/why/run/test.

## Deviations from spec
- **Smaller NMEA fixture instead of 24 hours.** The spec says "Record 24 hours of live data into a fixture file checked into Git LFS or stored in MinIO seed data." Recording 24 hours requires a sustained TCP connection to Kystverket from a developer machine; this is a real-world step the developer can perform once, and a Make target `make ais-record` will be added in phase 6. The phase 1 fixture is small enough to keep in Git directly and large enough to make `cargo test` exercise every parser path.
- **sqlx without compile-time macros.** The spec doesn't dictate; using `sqlx::query` + `query_as` (runtime-checked) keeps CI builds free of a live database, with no functional cost for this phase's query shapes.
- **Migrations via Flyway one-shot container, not init container.** A `restart: "no"` Flyway container that exits after running migrations is the same pattern as an init container for compose's purposes and keeps the dependency declarative.

## Verification
- `cargo fmt -- --check` clean.
- `cargo clippy -- -D warnings` clean on the workspace.
- `cargo test` green: parser unit tests on canonical fixture sentences, source replay test reads file end-to-end, store batch test against a docker-managed Postgres or skipped under `#[ignore]` when no DB is reachable.
- `docker compose up ais-ingestion` reaches healthy with replay file enabled.
- `psql ... -c "SELECT COUNT(*) FROM positions"` after 60 seconds of replay returns > 0.
- CI green for the new `rust.yml` workflow.
