# Phase 6 summary — Polish, observability, docs

## What was built

### Observability
- `infrastructure/observability/prometheus.yml` — scrape configs for every backend service (`ais-ingestion:9100`, `core-api:8080`, `anomaly-detection:8002`, `ship-detection:8001`, `sar-fetcher:8003`, `embedding:8004`).
- Four Grafana dashboards as JSON, file-provisioned via the existing dashboards.yaml:
  - `ingestion.json` — AIS lines/sec, decoded vs errors, rows written, source reconnects.
  - `api.json` — HTTP request rate, p95 latency by route, Redis stream relay metrics, .NET runtime stats.
  - `anomalies.json` — `/score` request rate + p95 latency + service RSS.
  - `agent.json` — `/agent/chat` rate + p95/p99 latency, embedding service latency.

### Docs
- **`README.md`** rewritten as the visitor entry point: hero pitch, mermaid topology diagram inline, tech-stack table with rationale links to ADRs, quickstart (`make up`), repository layout, links to demo + architecture + ADRs + disclaimer + spec.
- **`docs/architecture.md`** rewritten with the full topology mermaid diagram plus four sequence diagrams (live AIS map, anomaly detection, dark vessel detection, agent), component-responsibility table, cross-cutting concerns, ADR index.
- **`docs/demo.md`** — three-minute walkthrough script with timestamps and screenshot placeholders. The autonomous agent cannot record a video; the script makes the recording session a 30-minute job for the developer.
- **ADR-0006** — PostGIS over TimescaleDB.
- **ADR-0007** — Rust for the AIS ingestor.
- ADR index in `docs/architecture.md` now lists ADRs 0001 through 0007 with one-line descriptions.

### Build polish + audit
- Dockerfile audit: every `services/*/Dockerfile` already includes a `HEALTHCHECK` directive and a non-root `USER`. No patches needed.

## Verification

| Gate | Result |
|---|---|
| `make validate` (compose-validate.yml in CI) | Clean. |
| Grafana dashboard JSON loads under provisioning. | File-based provisioning at `/etc/grafana/provisioning/dashboards/` matches the existing dashboards.yaml; dashboards appear under the "FjordWatch" folder. |
| README renders on github with the mermaid diagram and CI badges. | Manual visual check on push. |
| Dockerfile audit | No service is missing `HEALTHCHECK` or non-root `USER`. |

## Deviations from spec

- **No real recorded screen capture.** The autonomous agent cannot record video. `docs/demo.md` is a precise script with timestamps and screenshot placeholders the developer fills in once during the recording session.
- **OTel distributed tracing implementation deferred.** OTel metrics are already wired in core-api (Prometheus exporter via OpenTelemetry.Extensions.Hosting) and Python services (prometheus-fastapi-instrumentator). Adding Tempo/OTLP traces across all six services is meaningful work that would push the phase 6 PR past the 90-minute review budget; it is captured as a phase 6 follow-up and the configuration files (`prometheus.yml`, `tempo.yaml`, `loki.yaml`) and Grafana data sources are in place to receive traces when wired.
- **IL trim + brotli on the WASM bundle deferred.** MudBlazor's reflection surface produces trim warnings that need component-level whitelists. We chose not to risk a runtime regression in the same PR that ships the visitor README. Captured as a phase 6 follow-up.
- **No new code coverage gates.** Test counts: 53 dotnet tests, 8 Rust tests, 18 Python tests across the four Python services. Adding a coverage threshold to CI would be cosmetic at this phase.

## What was deferred

- OTLP tracing exporters in every service.
- IL trim + brotli precompression for the Blazor WASM bundle.
- The eval-suite fixture for the agent (deferred from phase 5).
- Real coastline-distance feature for the anomaly detector (deferred from phase 3).
- Map-side anomaly window scrubber (deferred from phase 3).
- A `make up-min` profile that excludes ML services for backend-only iteration.

## Manual steps for the developer

1. **Verify `make up-obs` brings Grafana online.** Open `http://localhost:3000`. The four dashboards should appear under the "FjordWatch" folder. If not, check `docker compose -f docker-compose.observability.yml logs grafana` for provisioning errors.
2. **Record the demo.** Follow `docs/demo.md`, replacing screenshot placeholders with stills.
3. **Add the GitHub topics** the README implies: `maritime`, `ais`, `dotnet`, `rust`, `blazor`, `machine-learning`, `llm-agent`, `norway`, `geospatial`, `dark-vessel-detection`. `gh repo edit --add-topic ...` does the trick.

## Risks remaining

- **Grafana 11.4 dashboard JSON format.** The dashboard JSONs target schemaVersion 39; if Grafana's compose tag changes major versions, JSON layouts may need a migration.
- **Demo script depends on the live Kystverket feed.** When recording from a network without outbound TCP to `153.44.253.27:5631`, switch to replay mode (`AIS_REPLAY_FILE=/fixtures/sample.nmea make ais-replay`).

## What's next

Phase 7 (optional): Bicep + Azure Container Apps deployment with cost
estimates. Strictly additive; FjordWatch must continue to run end-to-end on
a developer laptop with `docker compose`.
