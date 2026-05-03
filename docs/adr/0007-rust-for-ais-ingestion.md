# 0007. Rust for AIS ingestion, not .NET

- **Status:** Accepted
- **Date:** 2026-05-03
- **Deciders:** Erdem Kilci

## Context

The AIS ingestor is a long-running, byte-level network service that reads a
sustained TCP stream of NMEA AIVDM/AIVDO sentences from Kystverket, decodes
each into a structured AIS message, upserts vessel/position rows into
Postgres, and publishes to a Redis Stream. The throughput is moderate (a few
thousand messages per second peak in coastal Norway), but the service must
run for days without restarts, recover from upstream drops, and keep memory
predictable.

## Decision

Implement the ingestor in **Rust** with `tokio` for the async runtime, the
`ais` crate for AIVDM decoding, `sqlx` for Postgres (runtime queries to
avoid a build-time database dependency), and `redis` for the Streams
publisher. Strict lints: `clippy::all` denied, `pedantic` and `nursery`
warned, `unsafe_code` forbidden.

## Considered alternatives

- **.NET 9 with `Kystverket/ais-dotnet`.** Pros: matches the developer's strongest stack; reuses the same SDK, build, and CI. Cons: GC pauses at the wrong moment can lose AIS messages while the socket buffer fills; the BCL's TCP socket types are heavier than `tokio::net::TcpStream`; tooling for byte-exact NMEA decoding is less mature than the Rust crate ecosystem.
- **Go.** Pros: simple goroutine model, fast compile. Cons: weaker type system for the message-shape variants we surface (Class A vs Class B vs static vs aid-to-navigation).
- **Python (asyncio).** Pros: fastest to prototype. Cons: the GIL caps single-process throughput; restarts cost more (slow imports); not a credibility signal for a senior backend role.

## Consequences

- **Positive:** ~50 MB RSS at full Norwegian-coast traffic, well below 1 vCPU. Predictable latency. Strong typing for AIS message variants. The Rust + `tokio` choice is a deliberate credibility signal for senior backend roles in Norwegian maritime/defense employers.
- **Negative:** adds Rust to the codebase. A second toolchain to install for contributors, a separate CI workflow (`.github/workflows/rust.yml`), longer first build. The team has to ramp on Rust borrow-checker idioms for non-trivial changes.
- **Follow-ups:** if the project ever ships a second ingestion source (e.g., satellite AIS via spire), replicate the pattern; resist the urge to fork .NET-land just because the team is faster there.

## References

- `ais` crate: https://crates.io/crates/ais
- `tokio` async runtime: https://tokio.rs/
- Kystverket AIS feed: https://www.kystverket.no/en/navigation-and-monitoring/ais/
