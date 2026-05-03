# Phase 0 plan — Foundation

## Goal
Anyone with Docker installed can clone the repo and run `make up` to bring the full stub stack online. CI validates compose configs.

## Files to create
1. `.gitignore` — Rust, .NET, Python, Node, IDE (`.vscode`, `.idea`), OS (`.DS_Store`), env files, build outputs.
2. `LICENSE` — MIT.
3. `.editorconfig` — UTF-8, LF, 4-space indent for code, 2-space for YAML/JSON/Markdown.
4. `DISCLAIMER.md` — independence statement, no operational use.
5. `README.md` — slimmed visitor-facing intro with quickstart.
6. `.env.example` — every variable used by future phases (Postgres creds, MinIO keys, LLM provider toggle, Copernicus placeholder).
7. `Makefile` — targets `up`, `down`, `logs`, `test`, `lint`, `format`, `seed`, `clean`.
8. `docker-compose.yml` — Postgres+PostGIS, Redis, MinIO, Ollama, plus one stub container per backend service (`ais-ingestion`, `core-api`, `ship-detection`, `anomaly-detection`, `sar-fetcher`, `web`). Stubs use `alpine` and print `ready` on a loop.
9. `docker-compose.observability.yml` — Grafana, Tempo, Loki, Prometheus skeleton (off by default).
10. `docs/architecture.md` — placeholder Mermaid diagram for high-level data flow.
11. `docs/SPEC.md` — symlink or copy of the spec for long-term reference (current `FjordWatch-SPEC.md` stays at root for now; will move in phase 6 polish).
12. `docs/data-sources.md` — table of sources with licenses (already enumerated in spec section 4).
13. `.github/workflows/compose-validate.yml` — runs `docker compose config` against both compose files on every push and PR.
14. `docs/adr/0000-template.md` — ADR template using MADR.
15. `CONTRIBUTING.md` — light, signals professionalism (deferred polish in phase 6 also adds detail).

## Deviations from spec
- Spec lists ADRs `0001-rust-for-ingestion.md`, `0002-postgis-vs-timescaledb.md`, etc. for phase 0. I will write the ADR template (`0000-template.md`) here and create the actual numbered ADRs alongside the phase that introduces each decision. Reason: phase-0 ADRs would be hand-wavy without the implementation context that phase 1+ provides.
- Stub containers use `busybox` with a 30s sleep loop and a tiny healthcheck script (`exit 0`) rather than full placeholder services. This keeps `make up` fast and makes "all services healthy" a real check.
- `docs/SPEC.md` will be a copy at phase-6 time, not phase 0, to avoid drift while the spec at root is the source of truth.

## Verification
- `docker compose -f docker-compose.yml config` returns exit 0.
- `docker compose -f docker-compose.yml -f docker-compose.observability.yml config` returns exit 0.
- `make up` brings all containers to healthy. `docker compose ps` shows healthy.
- `make down` cleans up.
- CI workflow green.
