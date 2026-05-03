# FjordWatch

Maritime intelligence platform for the Norwegian coast. FjordWatch ingests live AIS vessel tracking from the Norwegian Coastal Administration (Kystverket), correlates it with Sentinel-1 SAR satellite imagery to surface "dark vessels" that are not broadcasting AIS, runs trajectory anomaly detection, and exposes everything through a real-time Blazor dashboard with a natural-language LLM agent.

> Independent open-source portfolio project. Not affiliated with any organization. See [DISCLAIMER.md](./DISCLAIMER.md). Not for operational maritime surveillance.

## Status

Active development. Phase 0 (foundation) in progress. See [docs/plans](./docs/plans) for phase plans and summaries, and [FjordWatch-SPEC.md](./FjordWatch-SPEC.md) for the full specification.

## Tech stack

| Layer | Technology | Why |
|---|---|---|
| AIS ingestion | Rust + tokio | High-throughput byte-level network service with predictable latency |
| Core API | .NET 9 ASP.NET Core Minimal API | Strongest stack for the surface that talks to the UI |
| ML services | Python 3.12 + FastAPI | Native ecosystem for PyTorch, scikit-learn, ONNX |
| Frontend | Blazor WebAssembly + MudBlazor + Leaflet | Real-time map with SignalR push |
| Database | PostgreSQL 16 + PostGIS + pgvector | Spatial queries and embeddings in one place |
| Streaming | Redis Streams | Fan-out from ingestion to consumers without Kafka complexity |
| Object storage | MinIO (local), Azure Blob (cloud) | S3-compatible API, swapped via configuration |
| LLM agent | Semantic Kernel + Ollama (default) or Azure OpenAI | Provider abstraction, runs locally with no API costs |
| Observability | OpenTelemetry + Grafana + Tempo + Loki + Prometheus | One trace per request across all services |
| Orchestration | docker compose (local), Bicep + Azure Container Apps (optional cloud) | Local first, cloud optional |

## Quickstart

Prerequisites: Docker and `make` only.

```bash
git clone https://github.com/ErdemKilci/fjordwatch.git
cd fjordwatch
make env       # creates .env from .env.example
make up        # brings up the local stack
make ps        # verify everything is healthy
make logs      # tail logs
make down      # tear it all down
```

The optional observability stack (Grafana, Tempo, Loki, Prometheus) is started separately:

```bash
make up-obs
```

## Repository layout

```
fjordwatch/
  services/         # ais-ingestion (Rust), core-api (.NET), ml services (Python), web (Blazor)
  ml/               # shared utilities, dataset download scripts, MLflow
  infrastructure/   # bicep, compose, grafana
  tests/e2e/        # Playwright TypeScript end-to-end tests
  docs/             # architecture, ADRs, data sources, phase plans, demo script
  .github/workflows # CI per language
```

Service-level READMEs live alongside the code; see each `services/<name>/README.md` for what, why, run, and test.

## Data sources

All data sources are public and openly licensed. See [docs/data-sources.md](./docs/data-sources.md). FjordWatch relies on Kystverket AIS (NLOD), Copernicus Sentinel-1 SAR (Copernicus open license), and Met.no weather (NLOD).

## Documentation

- [Specification](./FjordWatch-SPEC.md): full project spec.
- [Architecture](./docs/architecture.md): high-level diagrams and component interactions.
- [Phase plans and summaries](./docs/plans): per-phase planning artefacts.
- [Architecture Decision Records](./docs/adr): non-trivial decisions with context.
- [Disclaimer](./DISCLAIMER.md): scope, licensing, and limits of use.

## License

[MIT](./LICENSE).
