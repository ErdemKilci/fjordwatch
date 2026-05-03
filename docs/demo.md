# FjordWatch — three-minute demo

This is the script for a three-minute walkthrough of FjordWatch, intended
for a recruiter/interview audience. It assumes a clean clone with Docker
running. Replace the screenshot placeholders with stills from your own
recording session.

## 0:00 — Setup (off-camera)

Before recording, do the prep that takes longer than the demo allows:

```bash
make env
make up
make up-obs
docker compose exec ollama ollama pull llama3.1:8b-instruct-q4_K_M
docker compose run --rm embedding \
    python -m embedding.ingest_corpus \
        --database-url "$DATABASE_URL" --embedding-url http://embedding:8004
```

Wait for `make ps` to show every service as "healthy" (or "Up" for the
one-shot `db-migrate`). Open three browser tabs:
1. `http://localhost:5000` — the Blazor map.
2. `http://localhost:3000` — Grafana with the four dashboards loaded.
3. `http://localhost:8080/healthz` — quick console for showing the API.

## 0:00 to 0:30 — The map

> *Screenshot: vessels animating on the Norwegian coast.*

Open `http://localhost:5000`. Vessels render as colored dots on a Leaflet
map with OpenSeaMap nautical overlay. Pan to the Oslofjord; markers update
in real time via a SignalR hub fed from a Redis Stream that the Rust
ingestor populates from the live Kystverket feed.

Click any cargo ship (red marker). The side panel shows MMSI, name,
destination, and a 24-hour track painted on the map. The same panel powers
the "Show on map" deep links from the Anomalies tab and the agent.

## 0:30 to 1:00 — Anomalies

> *Screenshot: Anomalies tab with a sortable list, a high-score row highlighted.*

Click the **Anomalies** button in the app bar. A MudBlazor table lists
recent anomaly scores. The score is a weighted blend of an Isolation
Forest over six engineered features and an LSTM autoencoder over a
64-step resampled trajectory. Click the location icon on a row; the map
focuses on that vessel and replays its 24-hour track for the suspicious
window.

Open the Anomalies dashboard in Grafana to show the scoring tick latency
and the anomaly score distribution.

## 1:00 to 1:30 — Dark vessels

> *Screenshot: SAR overlay on, red and blue markers visible.*

Back on the map. Toggle the **SAR overlay** in the top-left. Red markers
are SAR detections without a matching AIS broadcast in a 500 m / 30-minute
window; blue are matched, with the matched MMSI and the AIS distance/lag
in the tooltip. Toggle "Dark only" to filter server-side.

Open `docs/dark-vessel-limitations.md` in a new tab and pause for a beat:
this is the document that explains what "dark" does and does not mean. It
is mandatory reading for anyone who wants to use this overlay
operationally; FjordWatch surfaces signals, not conclusions.

## 1:30 to 2:30 — The agent

> *Screenshot: chat panel open with a question and a cited answer.*

Click the floating chat button bottom-right. Ask:

```
Are there any dark vessel detections in northern Norway today?
```

The agent replies in plain prose with a chip showing the tool it called
(`dark_vessels`) and the bbox + lookback used. Ask a regulatory question:

```
What does Norwegian regulation say about AIS reporting requirements
for fishing vessels?
```

The agent calls `search_regulations` over the pgvector RAG corpus and
quotes Sjøfartsdirektoratet text with a citation chip pointing back at the
source URL. Ask one more:

```
Show me cargo ships in the Oslofjord right now.
```

The agent calls `nearest_vessels` with explicit lat/lon/radius parameters,
which the citation panel surfaces. Every fact is grounded in a tool call;
nothing is hallucinated.

Switch tabs to Grafana and open the Agent dashboard: chat latency p95 + p99
and the per-tool call rate are live.

## 2:30 to 3:00 — The plumbing

> *Screenshot: Grafana ingestion dashboard with line rate climbing.*

Open the Ingestion dashboard. AIS lines/sec, decoded vs decode errors,
batches committed, source reconnects. Switch to the Core API dashboard:
HTTP request rate, p95 latency by route, the Redis stream relay throughput.

Wrap up with the `docs/architecture.md` mermaid diagram. Six services
(Rust, .NET, three Python ML/agent services, Blazor WASM) plus PostGIS,
Redis, MinIO, and Ollama. Provider abstraction lets the agent run against
local Ollama or Azure OpenAI by changing one env var. Every non-obvious
decision is recorded as an ADR.

## Closing line

"Six services, no Azure account required, every fact in an agent answer
cited back to the tool that produced it. The full spec, the ADRs, and a
plan + summary per phase are checked in. The repo is the documentation."
