# 0001. Dapper + Npgsql instead of EF Core for the FjordWatch.Api read path

- **Status:** Accepted
- **Date:** 2026-05-03
- **Deciders:** Erdem Kilci

## Context

FjordWatch's core API (phase 2) is read-heavy and spatial. It exposes three
queries against Postgres + PostGIS: bounding-box vessel lookup, vessel detail,
and a 24-hour track. None of these endpoints have aggregations, joins across
many tables, or change tracking; all of them benefit from raw SQL with
PostGIS-specific functions (`ST_MakeEnvelope`, `ST_X`, `ST_Y`,
`DISTINCT ON`).

EF Core has reasonable PostGIS support via `Npgsql.EntityFrameworkCore.PostgreSQL`
+ NetTopologySuite, but the spatial type system propagates through every layer
and adds a non-trivial NetTopologySuite dependency to anything that touches a
domain object.

## Decision

Use **Dapper 2.1+ on top of Npgsql 8** for the read path in
`FjordWatch.Infrastructure`. Domain types stay POCO records with primitive
lat/lon doubles. SQL lives next to the repository in raw string constants.

## Considered alternatives

- **EF Core with NetTopologySuite spatial types.** Pros: LINQ familiarity, change tracking if we ever need writes, schema migrations. Cons: every domain object grows a NTS dependency, query plans for `DISTINCT ON` style queries are harder to express in LINQ, EF startup cost penalizes container cold starts.
- **Dapper only (no Npgsql data source).** Pros: less ceremony. Cons: misses out on Npgsql's data source pooling and OpenTelemetry hooks (the v8+ data source object is the recommended integration point).
- **Hybrid: Dapper for reads, EF Core for the eventual write path.** Pros: best of both. Cons: phase 2 has no writes, and the writer for AIS data is a separate Rust service that uses sqlx. We will reconsider only if FjordWatch ever grows a CRUD admin surface in .NET.

## Consequences

- **Positive:** thin domain types, raw SQL is auditable, query plans match what `EXPLAIN` reports verbatim, container startup is fast, no NetTopologySuite dependency in WASM frontend builds.
- **Negative:** SQL strings are not refactor-safe; if a column is renamed in `services/db/migrations`, only an integration test catches it. We mitigate with the Testcontainers-based integration test gated behind `FJORDWATCH_RUN_INTEGRATION_TESTS`.
- **Follow-ups:** if phase 5 (LLM agent) needs write endpoints, decide then whether to add EF Core for those writes only or stay on Dapper with explicit upserts.

## References

- Dapper README and PostgreSQL examples: https://github.com/DapperLib/Dapper
- Npgsql data source docs: https://www.npgsql.org/doc/basic-usage.html
- PostGIS spatial functions: https://postgis.net/docs/reference.html
