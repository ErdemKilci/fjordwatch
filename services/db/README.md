# db — schema migrations

PostgreSQL schema for FjordWatch, applied by Flyway as a one-shot compose service (`db-migrate`).

## What

- `migrations/V1__init.sql` — vessels, positions (PostGIS), vessel_anomalies, sar_detections, vessel_tracks materialized view, and a trigger that keeps `vessels.last_seen` synchronized with the latest position.

Future migrations land as `V2__*.sql`, `V3__*.sql`, etc. Never edit a migration that has been applied to a shared database; add a follow-up migration instead.

## Why

A single source of truth for the schema, applied identically in dev, CI, and any future cloud deployment. Flyway's filename-based versioning is sufficient and language-agnostic, which keeps Rust (ingestion), .NET (core API), and Python (ML) consumers aligned without ORM-specific tooling.

## Run

```bash
make up                     # starts postgres + db-migrate; migrations run once
docker compose logs db-migrate
```

To re-apply against a clean database:

```bash
make clean                  # drops volumes
make up
```

## Test

The schema is exercised by every downstream service's integration tests. There is no standalone test for migrations; if a migration fails Flyway exits non-zero and the dependent services do not start.
