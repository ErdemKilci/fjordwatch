# Phase 6 plan — Polish, observability, docs

## Goal
The repo is recruiter-ready. A visitor lands on the README, sees a clear hero
screenshot and a 90-second mental model, runs `make up` once, and reaches a
working dashboard with vessels animating on a map and an agent answering
questions. Operators get a Grafana dashboard per concern. Every non-obvious
decision is recorded as an ADR.

## Files to create or update

### Observability
1. `infrastructure/observability/grafana/dashboards/ingestion.json` — AIS lines/sec, decode errors, batches/sec, source reconnects, partial fragments. Source: Prometheus on `ais_*_total`.
2. `infrastructure/observability/grafana/dashboards/anomalies.json` — score histogram, scoring tick latency, rows written per tick.
3. `infrastructure/observability/grafana/dashboards/agent.json` — chat latency, tool call rate by tool, citations per answer, rate-limit denials.
4. `infrastructure/observability/grafana/dashboards/api.json` — http_server_request_duration histogram, p50/p95/p99, redis stream relay messages-in/out/dropped.
5. `infrastructure/observability/grafana/provisioning/datasources/datasources.yaml` — Prometheus, Tempo, Loki targets.
6. `infrastructure/observability/grafana/provisioning/dashboards/dashboards.yaml` — file-based dashboard provisioning pointing at `/var/lib/grafana/dashboards/`.
7. `infrastructure/observability/prometheus.yml` — scrape jobs for `ais-ingestion:9100`, `core-api:8080`, `anomaly-detection:8002`, `ship-detection:8001`, `sar-fetcher:8003`, `embedding:8004`.
8. `infrastructure/observability/tempo.yaml` — OTLP receivers + retention.
9. `infrastructure/observability/loki.yaml` — Docker driver friendly basics.

### Tracing
10. `services/ais-ingestion/Cargo.toml` + `src/telemetry.rs` — wire `tracing-opentelemetry` + `opentelemetry-otlp` behind the existing `Metrics` install.
11. `services/core-api/FjordWatch.Api/Program.cs` — extend the OTel builder with `WithTracing(...)`, `AddOtlpExporter`, ASP.NET Core + HttpClient instrumentation.
12. Each Python service — add `opentelemetry-instrumentation-fastapi` and an OTLP exporter, gated by `OTEL_EXPORTER_OTLP_ENDPOINT` so dev runs without it work unchanged.

### Docs
13. `docs/architecture.md` rewritten with the full topology mermaid diagram (Postgres+PostGIS, Redis Streams, MinIO, Ollama, embedding, all six backend services, Blazor frontend, observability sidecars).
14. `README.md` rewritten as the visitor entry point.
15. `docs/demo.md` — three-minute walkthrough with timestamps.
16. `docs/adr/0006-postgis-over-timescaledb.md` and `docs/adr/0007-rust-for-ais-ingestion.md` — the implicit decisions made in phases 1 and 1, now documented with the implementation as evidence.

### Build polish
17. `services/web/FjordWatch.Web/FjordWatch.Web.csproj` — enable IL trimming (`PublishTrimmed=true`, `TrimMode=full`) and brotli precompression (`BlazorEnableCompression=true` is on by default; verify and document).
18. `services/web/nginx.conf` — serve `.br` precompressed assets when present.

### Dockerfile audit
19. Walk every Dockerfile in `services/*/Dockerfile`, verify `HEALTHCHECK` is present, the user is non-root, and `apt-get install` lines pin no security-impactful packages without versions. Patch any gaps.

## Deviations from spec

- **No actual recorded screen capture.** Spec says "record an actual screen capture and link it." The autonomous agent cannot record a screen capture. `docs/demo.md` lays out the step-by-step walkthrough with screenshot placeholders the developer can fill in once during the recording session.
- **IL trim is "best-effort".** Trimming Blazor WASM apps with MudBlazor can produce runtime errors at edge components. We enable trimming with `TrimmerSingleWarn=false` in CI so warnings surface; the developer can manually whitelist any reflection-only types if the bundle breaks. Documented in the README.
- **Tempo/Loki are wired but not deeply tuned.** Production retention, S3 backing, and ingester sharding are out of scope; the configs are dev-grade (filesystem storage, 7-day retention).

## Verification

| Gate | How |
|---|---|
| `make up-obs` reaches healthy. | Manual once. Grafana shows all four dashboards loaded; Tempo and Loki datasources resolve. |
| One trace from UI -> /agent/chat -> tool -> Postgres in Tempo. | Manual once. |
| `make build` builds web with IL trimming on (`PublishTrimmed=true`). | CI passes; bundle drops ~30%. |
| README renders with mermaid diagram on github. | Visual check. |
| All Dockerfiles audit clean. | Grep + manual. |

## Risks

- **IL trim breaks MudBlazor reflection.** If the trim dump shows warnings, fall back to `TrimMode=partial` or whitelist `MudBlazor.*` in the trim roots. Phase 6 polish accepts this trade-off and documents it.
- **OTel auto-instrumentation overhead on Ollama-bound traces.** Tracing the LLM round-trip adds ~5 ms per call; acceptable.
- **Grafana dashboard JSON drifts with Grafana versions.** We pin Grafana to a single major in compose so JSONs do not unload silently.
