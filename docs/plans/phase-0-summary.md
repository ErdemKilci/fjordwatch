# Phase 0 summary — Foundation

## What was built

| Artefact | Purpose |
|---|---|
| `.gitignore` | Rust, .NET, Python, Node, IDE, OS, env files, ML and data artefacts. |
| `LICENSE` | MIT, dated 2026 to match the assistant's knowledge cutoff window. |
| `.editorconfig` | UTF-8, LF, 4-space code, 2-space YAML/JSON/Markdown, tabs in Makefiles. |
| `DISCLAIMER.md` | Independence statement, license attributions, no operational use clause. |
| `README.md` | Visitor-facing intro, tech stack table, quickstart, repo layout. |
| `CONTRIBUTING.md` | Workflow, conventional commits, coding standards, testing rules. |
| `.env.example` | Every variable downstream services will need (Postgres, Redis, MinIO, Ollama, Azure OpenAI placeholders, AIS source, Copernicus placeholders, OTLP endpoint, web URLs). |
| `Makefile` | `up`, `down`, `logs`, `ps`, `test`, `lint`, `format`, `seed`, `clean`, `validate`, `reset`, `env`, `build`, `pull`, `restart`. Auto-detects `docker compose` vs `docker-compose`. |
| `docker-compose.yml` | Postgres+PostGIS, Redis, MinIO, Ollama, plus busybox stubs for `ais-ingestion`, `core-api`, `ship-detection`, `anomaly-detection`, `sar-fetcher`, `web`. Stubs use `depends_on` with `service_healthy` so the dependency graph is real. |
| `docker-compose.observability.yml` | Prometheus, Tempo, Loki, Grafana (off by default), bind-mounted to placeholder configs. |
| `infrastructure/observability/` | Minimal config for Prometheus, Tempo, Loki, Grafana (datasources + dashboards provider). Phase 6 fills in scrape targets, retention, dashboards. |
| `docs/architecture.md` | Mermaid topology diagram, component responsibility table, cross-cutting concerns. |
| `docs/data-sources.md` | License register for Kystverket AIS, Met.no, BarentsWatch, Sjøfartsdirektoratet, Copernicus, ML datasets. |
| `docs/adr/0000-template.md` | MADR-style template for future ADRs. |
| `.github/workflows/compose-validate.yml` | Validates both compose files on push and PR. |

## What was skipped or deferred

- **Phase-listed ADRs (rust-for-ingestion, postgis-vs-timescaledb, semantic-kernel-vs-langchain, ollama-default).** Deferred to the phases that introduce each decision. Writing them now would be hand-wavy without implementation context; writing them with the implementation grounds them in real tradeoffs.
- **Real service Dockerfiles.** Phase 0 ships busybox stubs by design. Each subsequent phase replaces its stub.
- **Observability runtime configs.** The bind-mounted YAMLs contain enough to start, but Prometheus has no scrape targets, Grafana has no dashboards, and Tempo has no real retention policy. Phase 6 fills these in.

## Definition of done

| Check | Status | Evidence |
|---|---|---|
| `git clone && make up` works on a clean machine with only Docker installed. | Verified by config; runtime not run locally (see fallback). | `make validate` passes. Image references are pinned to public tags that can be pulled by any Docker installation. |
| `docker compose ps` shows all stub services healthy. | Cannot exercise locally; deferred to first developer run. | Stubs use `healthcheck: ["CMD","true"]` with conservative intervals; healthy state is reached almost immediately once started. |
| CI passes. | Green. | `gh run list --branch main` shows `compose-validate / push / success` for `feat(infra): scaffold phase 0 foundation` (run id 25278320608, 11s). |

## Fallbacks taken

- **No live `make up` execution.** Docker Desktop is not running for the current shell user (`deverdem`) on this machine; the daemon socket symlink at `/var/run/docker.sock` points to a different user account. The compose syntactic check (`make validate`) passes locally and in CI, which is the spec-mandated CI gate for phase 0. The first time the developer runs `make up` themselves with Docker Desktop running, they should see all six stubs plus four infra services reach healthy. If they do not, the issue is most likely image pull credentials or network egress; both are local environment concerns, not code defects.

## Risks remaining

- **Stub-to-real swap pressure.** Each phase replacing a stub must keep the same service name and network position so dependent services do not break. The plan files for phases 1+ will explicitly call this out.
- **Image versions drift.** Pinned tags (e.g., `postgis/postgis:16-3.4`, `redis:7-alpine`, `minio/minio:RELEASE.2024-12-18T13-15-44Z`, `ollama/ollama:0.4.7`) can become unavailable. A phase 6 follow-up will enable Dependabot for compose files.
- **Observability bind mounts.** If the developer renames or deletes any file under `infrastructure/observability/`, `make up-obs` will fail at container start (compose-config does not validate the contents). Phase 6 will add a smoke test that brings up the obs stack and queries each datasource.

## Manual steps for the developer

1. Run `make env && make up` once locally to confirm all containers reach healthy.
2. Verify the GitHub repo has topics set: `maritime`, `ais`, `dotnet`, `rust`, `blazor`, `machine-learning`, `llm-agent`, `norway`, `geospatial`, `dark-vessel-detection`. The phase 8 README mentions these; the assistant cannot set them without an additional permission and intentionally deferred to keep the repo settings under the developer's control. Set with `gh repo edit --add-topic maritime --add-topic ais ...` when convenient.

## What's next

Phase 1 starts now: AIS ingestion service in Rust, Postgres schema with migrations, Redis Streams publisher, replay mode for tests, fixture-based unit tests, and integration into the compose graph (replacing the busybox stub for `ais-ingestion`).
