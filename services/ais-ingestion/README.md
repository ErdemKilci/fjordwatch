# ais-ingestion

Rust service that pulls AIS NMEA sentences from the Norwegian Coastal
Administration (Kystverket) public feed or a recorded fixture, decodes them,
upserts vessel + position rows into Postgres, and publishes each decoded
message to a Redis Stream for downstream consumers.

## Why Rust here

AIS arrives as a high-volume, line-oriented binary-encoded stream. The job is
mostly parse, normalize, and forward, with strict latency and zero tolerance
for parser-induced data loss. Rust gives us a memory-safe, single-binary,
low-overhead ingestor that holds steady under burst load and runs on tiny
container resources (under 50 MB RSS, well under one CPU at typical Norwegian
coastal traffic levels).

## Pipeline

```
source (TCP or replay file)
   -> raw line channel
      -> decoder (ais crate)
         -> Redis Stream (publish)
         -> Postgres writer (batched)
```

Each stage is its own tokio task connected by bounded mpsc channels, so back
pressure from a slow Postgres or Redis pauses the source rather than dropping
messages.

## Run locally

```bash
# 1. From the repo root, bring up Postgres, Redis, and the migrator.
make up

# 2. Run the service against the live Kystverket feed (default).
make ais-replay   # or: docker compose up -d ais-ingestion

# 3. Tail logs and inspect.
docker compose logs -f ais-ingestion
psql "$DATABASE_URL" -c "SELECT COUNT(*) FROM positions;"
```

To replay the bundled fixture instead of the live socket, set
`AIS_REPLAY_FILE=/fixtures/sample.nmea` before bringing the service up. A
larger 24-hour fixture is captured via `make ais-record` (added in phase 6).

## Configuration

All settings come from environment variables. Defaults match the docker
compose stack.

| Variable | Default | Purpose |
|---|---|---|
| `DATABASE_URL` | n/a (required) | Postgres DSN |
| `REDIS_URL` | `redis://redis:6379/0` | Redis URL |
| `AIS_STREAM` | `ais:positions` | Redis Stream key |
| `AIS_SOURCE_HOST` | `153.44.253.27` | Kystverket NMEA host |
| `AIS_SOURCE_PORT` | `5631` | Kystverket NMEA port |
| `AIS_REPLAY_FILE` | unset | Path to recorded NMEA file. When set, bypasses the live socket. |
| `AIS_REPLAY_DELAY_MS` | `0` | Sleep between replay lines |
| `AIS_BATCH_SIZE` | `200` | Postgres flush batch size |
| `AIS_RECONNECT_INITIAL_BACKOFF_MS` | `500` | Live reconnect floor |
| `AIS_RECONNECT_MAX_BACKOFF_MS` | `30000` | Live reconnect ceiling |
| `AIS_METRICS_LISTEN` | `0.0.0.0:9100` | /healthz + /metrics bind |

## Test

```bash
cargo fmt --all -- --check
cargo clippy --workspace --all-targets -- -D warnings
cargo test --workspace
```

The CI workflow `.github/workflows/rust.yml` runs the same three commands on
every push and PR.

Unit tests cover the decoder normalization paths (class A position, type 21
aid-to-navigation rejection, ASCII trimming) and the ETA folding logic. An
integration test in `tests/replay_fixture.rs` runs the bundled NMEA fixture
through the parser end-to-end without any external services.

## Database

The schema lives at `services/db/migrations/V1__init.sql` and is applied by
the one-shot `db-migrate` Flyway compose service. The writer never touches
DDL at runtime.

## Observability

* `GET /healthz` returns `200 ok` once the binary has booted.
* `GET /metrics` exposes Prometheus counters: `ais_lines_in_total`,
  `ais_decoded_total`, `ais_decode_errors_total`, `ais_rows_written_total`,
  `ais_batches_committed_total`, `ais_publish_errors_total`,
  `ais_source_reconnects_total`, `ais_source_connect_errors_total`.
* Logs are JSON via `tracing_subscriber`. Set `RUST_LOG` to adjust levels.
