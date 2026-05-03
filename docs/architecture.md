# Architecture

FjordWatch is a six-service maritime intelligence platform for the Norwegian
coast. This document is the entry point for reviewers; it describes the
runtime topology, the data flow per feature, and where each non-trivial
decision is documented.

## Runtime topology

```mermaid
flowchart LR
    subgraph external[External public sources]
        K[Kystverket AIS<br/>TCP NMEA]
        C[Copernicus<br/>Sentinel-1 SAR]
    end

    subgraph ingest[Ingestion]
        AIS[ais-ingestion<br/>Rust + tokio]
        SAR[sar-fetcher<br/>Python + rasterio]
    end

    subgraph store[Stateful infrastructure]
        PG[(PostgreSQL 16<br/>PostGIS + pgvector)]
        RS[(Redis Streams + cache)]
        S3[(MinIO / Azure Blob)]
    end

    subgraph ml[ML + agent services]
        SD[ship-detection<br/>YOLOv8 ONNX]
        AD[anomaly-detection<br/>IsoForest + LSTM AE]
        EM[embedding<br/>multilingual-e5-large]
    end

    subgraph app[Application]
        API[core-api<br/>.NET 9 Minimal API<br/>+ agent orchestrator<br/>+ SignalR hub]
        WEB[web<br/>Blazor WASM + Leaflet]
    end

    subgraph llm[LLM provider]
        OL[Ollama<br/>llama3.1 8B]
        AZ[Azure OpenAI<br/>GPT-4o]
    end

    subgraph obs[Observability]
        PR[Prometheus]
        TE[Tempo]
        LO[Loki]
        GR[Grafana]
    end

    K --> AIS
    C --> SAR
    AIS --> PG
    AIS --> RS
    SAR --> S3
    SAR --> SD
    SD --> PG
    PG --> AD
    AD --> PG

    API --> PG
    RS --> API
    EM --> API
    API <--> OL
    API <--> AZ

    WEB <-->|SignalR / REST| API

    AIS -.metrics.-> PR
    API -.metrics.-> PR
    AD -.metrics.-> PR
    SD -.metrics.-> PR
    SAR -.metrics.-> PR
    EM -.metrics.-> PR
    API -.traces.-> TE
    PR --> GR
    TE --> GR
    LO --> GR
```

## Data flow per feature

### Live AIS map

```mermaid
sequenceDiagram
    participant Kystverket
    participant ais as ais-ingestion (Rust)
    participant pg as Postgres
    participant rs as Redis Stream
    participant api as core-api (.NET)
    participant web as Blazor WASM
    Kystverket->>ais: NMEA AIVDM lines (TCP)
    ais->>pg: vessel + position upserts (batched, transactional)
    ais->>rs: XADD ais:positions
    rs->>api: RedisStreamRelay (consumer group)
    api->>web: SignalR positionUpdate (viewport-filtered, rate-limited)
    web->>api: GET /vessels?bbox=... (on map move)
    api->>pg: Dapper bbox query
    pg-->>api: vessel rows
    api-->>web: JSON
```

### Anomaly detection

```mermaid
sequenceDiagram
    participant sched as anomaly-detection scheduler (every 10 min)
    participant pg as Postgres
    participant ens as Ensemble (IsoForest + LSTM-AE)
    participant api as core-api
    participant web as Blazor WASM
    sched->>pg: SELECT positions WHERE ts > now() - 6h
    pg-->>sched: per-MMSI windows
    sched->>ens: features + sequence
    ens-->>sched: score + iso_score + lstm_score + contributing
    sched->>pg: INSERT vessel_anomalies ... ON CONFLICT DO NOTHING
    web->>api: GET /anomalies?since=&minScore=
    api->>pg: Dapper read
    pg-->>api: rows
    api-->>web: JSON; click → focus map at vessel
```

### Dark vessel detection

```mermaid
sequenceDiagram
    participant cron as sar-fetcher scheduler
    participant cop as Copernicus Catalogue
    participant minio as MinIO
    participant sd as ship-detection
    participant pg as Postgres
    cron->>cop: search recent Sentinel-1 GRDs over Norway
    cop-->>cron: scene IDs + URIs
    cron->>cron: rasterio open + tile (1024x1024 sigma0 PNG + sidecar)
    cron->>minio: PUT tiles + sidecars
    cron->>sd: POST /detect with tile URIs
    sd->>minio: GET tile + sidecar
    sd->>sd: ONNX inference (YOLOv8) + bbox to WGS84
    sd->>pg: INSERT sar_detections
    sd->>pg: correlator queries positions within 500m / 30 min
    sd->>pg: UPDATE matched_mmsi / match_distance_m / match_lag_s / is_dark
```

### LLM agent

```mermaid
sequenceDiagram
    participant user as User (chat panel)
    participant api as core-api /agent/chat
    participant orch as AgentOrchestrator
    participant prov as IChatProvider (Ollama or Azure)
    participant tool as IAgentTool
    participant pg as Postgres
    user->>api: POST { message }
    api->>orch: AnswerAsync
    orch->>prov: complete(system, user)
    prov-->>orch: { tool: "...", args: { ... } }
    orch->>tool: InvokeAsync(args)
    tool->>pg: parameterized query
    pg-->>tool: rows
    tool-->>orch: ToolResult { summary, citation }
    orch->>prov: complete(system, user, assistant, tool result, "write final answer")
    prov-->>orch: prose answer
    orch-->>api: AgentResponse { reply, citations }
    api-->>user: JSON
```

## Component responsibilities

| Component | Language | Responsibility |
|---|---|---|
| `ais-ingestion` | Rust | Pull NMEA AIVDM/AIVDO from Kystverket, decode, upsert vessels and positions, publish to Redis Stream. |
| `core-api` | .NET 9 | REST surface, SignalR hub, agent orchestrator with tools over the data layer. |
| `ship-detection` | Python | YOLOv8 ONNX inference on Sentinel-1 SAR tiles plus AIS correlator. |
| `anomaly-detection` | Python | Trajectory features + Isolation Forest + LSTM autoencoder ensemble. |
| `sar-fetcher` | Python | Scheduled Sentinel-1 fetch + rasterio tiling + MinIO upload. |
| `embedding` | Python | multilingual-e5-large embeddings for the agent's RAG corpus. Stub mode for CI. |
| `web` | Blazor WASM | Real-time map, anomaly tab, dark vessels overlay, agent chat panel. |
| `postgres` | Postgres + PostGIS + pgvector | Spatial vessel and detection storage, regulation embeddings. |
| `redis` | Redis | AIS fan-out (Streams), vessel cache, rate limiting. |
| `minio` | MinIO | SAR tile storage, fixture archive. |
| `ollama` | Ollama | Default local LLM serving. |

## Cross-cutting concerns

- **Configuration:** every variable in `.env.example`. Each service consumes only the variables it needs; defaults are dev-grade.
- **Observability:** Prometheus scrape on `/metrics` of every service. Grafana dashboards in `infrastructure/observability/grafana/dashboards/`. Tempo traces and Loki logs ship via OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- **Health and readiness:** every service exposes `/healthz` (liveness) and `/readyz` (readiness). Compose healthchecks gate dependent services.
- **Security boundaries:** no inbound auth in v1. The agent endpoint applies an in-process rate limit; production deployments behind a reverse proxy add network-level auth.
- **Citations:** every fact returned by the agent carries a Citation pointing back to the tool that produced it; see [`agent-honesty.md`](agent-honesty.md).
- **Limits and disclaimers:** see [`dark-vessel-limitations.md`](dark-vessel-limitations.md) and [`DISCLAIMER.md`](../DISCLAIMER.md).

## Decisions

Non-trivial choices are recorded as ADRs in [`adr/`](./adr/):

| ADR | Decision |
|---|---|
| [0001](./adr/0001-dapper-over-ef-for-readpath.md) | Dapper + Npgsql for the .NET read path, not EF Core. |
| [0002](./adr/0002-isoforest-lstm-ae-ensemble.md) | IsoForest + LSTM autoencoder ensemble for anomaly scoring. |
| [0003](./adr/0003-rasterio-vs-gdal-bindings.md) | rasterio over the GDAL Python bindings. |
| [0004](./adr/0004-custom-orchestrator-vs-semantic-kernel.md) | Custom one-shot orchestrator instead of the SK Ollama connector. |
| [0005](./adr/0005-pgvector-vs-qdrant.md) | pgvector for the RAG corpus, not a separate vector DB. |
| [0006](./adr/0006-postgis-over-timescaledb.md) | PostGIS for spatial + time-series vessel data, not TimescaleDB. |
| [0007](./adr/0007-rust-for-ais-ingestion.md) | Rust for the AIS ingestor, not .NET. |
