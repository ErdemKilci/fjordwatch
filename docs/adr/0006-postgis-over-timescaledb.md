# 0006. PostGIS for spatial + time-series, not TimescaleDB

- **Status:** Accepted
- **Date:** 2026-05-03
- **Deciders:** Erdem Kilci

## Context

FjordWatch stores two high-cardinality time-series tables (`positions`,
`vessel_anomalies`) alongside spatial tables (`vessels`, `sar_detections`)
and a vector-search corpus (`regulation_chunks`). All three workloads touch
the same database. We had to pick between:

- **Pure PostGIS** on stock Postgres 16, with B-tree indexes on `(mmsi, ts)` and a GIST index on `geom`.
- **TimescaleDB** with hypertables on `positions` and a separate PostGIS path for the spatial columns.
- **Two databases** (TimescaleDB for time-series, Postgres + PostGIS for spatial).

## Decision

Use **PostGIS on stock Postgres 16** for everything. The phase-1 schema
(`V1__init.sql`) creates `positions` with a composite primary key on
`(mmsi, ts)`, a `GEOGRAPHY(POINT, 4326)` column, a GIST spatial index, and a
DESC index on `ts`. PostGIS + pgvector run in the same `postgis/postgis:16-3.4`
image already shipped with `apt-get install postgresql-16-pgvector`.

## Considered alternatives

- **TimescaleDB hypertables.** Pros: automatic chunking by time, compression, continuous aggregates, retention policies built in. Cons: docker image cycle is separate from PostGIS-base; mixing hypertables and PostGIS spatial joins in one query is supported but adds complexity; the ingest rate FjordWatch needs (a few thousand inserts per second peak) sits well within stock Postgres on a laptop. Time-series compression is genuinely valuable but not in v1.
- **Two databases (Timescale + PostGIS).** Two backups, two connection strings, joins across processes. Hard veto for a portfolio piece that should "just work" on a developer laptop.
- **Druid / ClickHouse.** Overkill for AIS rates; no spatial story.

## Consequences

- **Positive:** one connection string, one backup, one extension graph. Spatial joins (`ST_DWithin`, `ST_X`, `ST_Y`) compose naturally with the time-series filters (`ts > now() - interval '6 hours'`). pgvector lives in the same database, so the agent's RAG retrieval does not introduce a second datastore (ADR-0005).
- **Negative:** at sustained Norwegian-coastal AIS rates (~10 M positions per day), the unmaintained partitioning means table bloat grows indefinitely. Mitigation: phase 6 polish wires a partman-driven monthly partition + retention policy. For now, the schema fits comfortably in a developer laptop's storage budget.
- **Follow-ups:** if/when ingest rates climb past current laptop limits, revisit migrating just `positions` to a Timescale hypertable. The schema's primary key shape is hypertable-compatible.

## References

- PostGIS: https://postgis.net/
- TimescaleDB vs vanilla Postgres benchmark, Citus blog: https://www.citusdata.com/blog/2018/09/24/about-postgres-time-series/
