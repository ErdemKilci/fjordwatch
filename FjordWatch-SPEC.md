# FjordWatch — Maritime Intelligence Platform

> **Project spec for Claude Code.** This document is the single source of truth. Read it fully before starting. Build the project module by module in the order given. After each module, run the verification checklist for that module before moving on.

---

## 0. What you are building

**FjordWatch** is a production-grade maritime intelligence platform for the Norwegian coast. It ingests live AIS (vessel tracking) data from the Norwegian Coastal Administration (Kystverket), correlates it with Sentinel-1 satellite radar imagery to detect "dark vessels" (ships not broadcasting AIS), runs anomaly detection on vessel trajectories, and exposes everything through a real-time web dashboard with a natural-language LLM agent.

This is a **portfolio project** designed to be impressive enough to land interviews at Kongsberg, DNV, Cognite, Maritime Robotics, Equinor, Bouvet, Sopra Steria, Computas, and Kystverket itself.

**Non-goal:** This is not a commercial product. It is a public, open-source educational project. It must include a clear disclaimer that it is independent and not affiliated with any organization mentioned, including TOMRA ASA (the developer's current employer).

---

## 1. Hard constraints

These are non-negotiable. Do not deviate without asking the developer first.

1. **Use the best tool for each job.** The developer's current resume mentions C#, Python, .NET, Blazor, Docker, Azure, but this project should use whatever stack is genuinely best for each component, even if it adds new technologies (Rust, Go, TypeScript, etc.). The goal is to learn and to look credible to senior engineers, not to stay inside a comfort zone.
2. **Everything must run locally first.** The full stack must work via `docker compose up` on a developer laptop with no Azure account required. Cloud deployment is optional and additive.
3. **Use only public, free, properly-licensed data.** Norwegian Coastal Administration AIS data is under NLOD (Norsk lisens for offentlige data). Sentinel-1 SAR is under Copernicus open license. Met.no weather data is under NLOD. Cite licenses in `docs/data-sources.md`.
4. **No real-world surveillance use.** This is a research/learning project. The README and UI must include disclaimers stating the system is not for operational use, law enforcement, or military targeting.
5. **No copyrighted datasets.** Use only datasets with permissive licenses (MIT, Apache, CC-BY, CC0, public domain, NLOD, Copernicus). Document every dataset's license.
6. **Reproducibility.** Every ML model must have a training script, a fixed random seed, a pinned `requirements.txt` or `pyproject.toml`, and a `make train` (or equivalent) entry point.
7. **Do not commit secrets.** Use `.env.example` files. Never commit real API keys.
8. **Code must be production-quality.** Linting, formatting, type hints, tests, CI. Not throwaway scripts.

---

## 2. Final tech stack (chosen for fitness, not familiarity)

These choices are deliberate. If you think a different choice is materially better, propose it before changing.

### Backend services
- **AIS ingestion service:** **Rust** with `tokio` for the TCP socket consumer and NMEA decoding. Rationale: this is a high-throughput, long-running, byte-level network service. Rust gives memory safety, predictable latency, and tiny container images. This is also a credibility signal for senior backend roles. If Rust ramp-up is too costly mid-project, fall back to **.NET 9** with `Kystverket/ais-dotnet` as the base library, but document the tradeoff.
- **Core API (vessel queries, anomaly results, agent proxy):** **.NET 9** with **ASP.NET Core Minimal API**. The developer is strong here; this is the right place to leverage that.
- **ML services (ship detection, anomaly detection, SAR processing):** **Python 3.12** with **FastAPI**. Each ML service is its own container exposing a small REST surface.

### Frontend
- **Web app:** **Blazor WebAssembly** (.NET 9) with **MudBlazor** for components and **Leaflet.js** via JS interop for the map. SignalR for real-time vessel updates. Rationale: leverages the developer's strongest stack for the most user-facing piece.

### Data
- **Primary database:** **PostgreSQL 16** with **PostGIS** (spatial) and **pgvector** (embeddings).
- **Object storage:** **MinIO** locally (S3-compatible), **Azure Blob Storage** in cloud deployment. Used for SAR tiles and raw AIS log archives.
- **Message bus:** **Redis Streams** for fan-out from AIS ingestion to consumers (anomaly detector, SignalR hub). Lightweight, no Kafka complexity.
- **Cache:** **Redis** (same instance, different keyspace) for vessel lookups and rate limiting.

### Machine learning
- **Ship detection in SAR imagery:** **YOLOv8** (Ultralytics). Train on the **Airbus Ship Detection Challenge** dataset (Kaggle, free, CC-BY-NC for non-commercial — note this and switch to **HRSC2016** or **ShipRSImageNet** if commercial-friendliness is needed).
- **Trajectory anomaly detection:** Ensemble of:
  - **Isolation Forest** (scikit-learn) on hand-crafted trajectory features (speed delta, heading variance, stop duration, distance from coast).
  - **LSTM autoencoder** (PyTorch) on resampled trajectory sequences for sequence-level anomalies.
  - Final score = weighted average. Both models registered in MLflow.
- **Embedding model for RAG:** **multilingual-e5-large** (open weights, MIT license) for Norwegian + English documents.

### LLM agent
- **Default:** Local model via **Ollama** (Llama 3.1 8B Instruct) for development and demos with no API costs.
- **Optional cloud:** Azure OpenAI (GPT-4o) toggled by environment variable for higher quality. Demonstrate provider abstraction.
- **Framework:** **Semantic Kernel** (.NET) in the Core API. Tools: `query_vessels`, `get_trajectory`, `lookup_anomalies`, `search_regulations`, `nearest_vessels`. Reasoning: keeps the agent inside the .NET runtime alongside the API; demonstrates Microsoft's agent framework which Norwegian enterprises increasingly adopt.

### Infrastructure
- **Local:** `docker compose` for everything (Postgres, Redis, MinIO, Ollama, all services).
- **CI:** **GitHub Actions** with separate jobs for Rust, .NET, Python, and the frontend.
- **Cloud (optional):** **Azure Container Apps** with **Bicep** IaC. Document the path; do not require it for the project to be considered complete.
- **Observability:** **OpenTelemetry** instrumentation across services. Local stack: **Grafana + Tempo + Loki + Prometheus** in compose.

### Testing
- **.NET:** xUnit + FluentAssertions + Testcontainers.
- **Python:** pytest + pytest-asyncio + httpx.
- **Rust:** built-in `cargo test` + `wiremock` for the TCP source.
- **End-to-end:** **Playwright** (TypeScript) for the Blazor UI. Robot Framework optional for vessel-data scenarios since the developer uses it at TOMRA.

---

## 3. Repository structure

Create exactly this layout. Place a short `README.md` in each top-level service folder explaining what it does, how to run it locally, and how to test it.

```
fjordwatch/
├── README.md                            # this file
├── LICENSE                              # MIT
├── .gitignore
├── .editorconfig
├── docker-compose.yml                   # full local stack
├── docker-compose.observability.yml     # optional Grafana/Tempo/Loki
├── .env.example
├── Makefile                             # top-level orchestration
│
├── docs/
│   ├── architecture.md
│   ├── data-sources.md
│   ├── deployment.md
│   ├── adr/                             # architecture decision records
│   │   ├── 0001-rust-for-ingestion.md
│   │   ├── 0002-postgis-vs-timescaledb.md
│   │   ├── 0003-semantic-kernel-vs-langchain.md
│   │   └── 0004-ollama-default.md
│   └── images/                          # diagrams (mermaid sources + rendered SVG)
│
├── services/
│   ├── ais-ingestion/                   # Rust
│   │   ├── Cargo.toml
│   │   ├── src/
│   │   │   ├── main.rs
│   │   │   ├── nmea.rs                  # NMEA/AIVDM parser (or use `ais-rs` crate)
│   │   │   ├── kystverket.rs            # TCP client
│   │   │   ├── store.rs                 # Postgres writer
│   │   │   ├── stream.rs                # Redis Streams publisher
│   │   │   └── telemetry.rs
│   │   ├── tests/
│   │   └── Dockerfile
│   │
│   ├── core-api/                        # .NET 9
│   │   ├── FjordWatch.Api/
│   │   ├── FjordWatch.Domain/           # entities, value objects
│   │   ├── FjordWatch.Infrastructure/   # EF Core, repositories
│   │   ├── FjordWatch.Agent/            # Semantic Kernel kernel + tools
│   │   ├── FjordWatch.Api.Tests/
│   │   └── Dockerfile
│   │
│   ├── ship-detection/                  # Python FastAPI
│   │   ├── pyproject.toml
│   │   ├── src/ship_detection/
│   │   │   ├── api.py
│   │   │   ├── inference.py
│   │   │   ├── model.py                 # YOLOv8 wrapper
│   │   │   └── sar_preprocess.py
│   │   ├── tests/
│   │   ├── notebooks/
│   │   │   └── train.ipynb
│   │   ├── scripts/
│   │   │   ├── download_dataset.sh
│   │   │   └── train.py
│   │   └── Dockerfile
│   │
│   ├── anomaly-detection/               # Python FastAPI
│   │   ├── pyproject.toml
│   │   ├── src/anomaly_detection/
│   │   │   ├── api.py
│   │   │   ├── features.py              # trajectory feature engineering
│   │   │   ├── isoforest.py
│   │   │   ├── lstm_ae.py
│   │   │   └── ensemble.py
│   │   ├── scripts/train.py
│   │   ├── tests/
│   │   └── Dockerfile
│   │
│   ├── sar-fetcher/                     # Python scheduled worker
│   │   ├── pyproject.toml
│   │   ├── src/sar_fetcher/
│   │   │   ├── copernicus_client.py
│   │   │   ├── tiler.py                 # GDAL tiling
│   │   │   └── scheduler.py
│   │   └── Dockerfile
│   │
│   └── web/                             # Blazor WebAssembly
│       ├── FjordWatch.Web/
│       │   ├── Components/
│       │   ├── Pages/
│       │   ├── Services/                # SignalR client, API client
│       │   ├── wwwroot/
│       │   │   └── js/leaflet-interop.js
│       │   └── Program.cs
│       └── Dockerfile
│
├── ml/
│   ├── shared/                          # common Python utilities (Python package)
│   ├── datasets/                        # download scripts, NOT raw data
│   └── mlflow/                          # MLflow tracking server compose snippet
│
├── infrastructure/
│   ├── bicep/                           # Azure IaC (optional path)
│   ├── compose/                         # supporting compose files
│   └── grafana/                         # dashboards as JSON
│
├── tests/
│   └── e2e/                             # Playwright TypeScript
│
└── .github/
    └── workflows/
        ├── rust.yml
        ├── dotnet.yml
        ├── python.yml
        ├── web.yml
        ├── e2e.yml
        └── docker-publish.yml
```

---

## 4. Data sources (verified accessible)

Document each in `docs/data-sources.md` with: license, access method, rate limits, schema link, citation.

| Source | What | Access | License |
|---|---|---|---|
| Kystverket AIS | Live vessel positions, Norwegian EEZ | TCP `153.44.253.27:5631`, raw NMEA AIVDM/AIVDO | NLOD |
| Copernicus Sentinel-1 | C-band SAR imagery | `sentinelsat` Python lib via Copernicus Data Space Ecosystem | Copernicus open |
| Met.no Locationforecast | Wind, waves, weather | REST `api.met.no/weatherapi/locationforecast/2.0` | NLOD |
| BarentsWatch | Public maritime services | Public APIs, registration for some endpoints | Mixed, document each |
| Sjøfartsdirektoratet | Vessel registry lookup | Public web search, scrape responsibly with caching | Public records |

For ML training:
- **Airbus Ship Detection** (Kaggle): note CC-BY-NC, use only for non-commercial demo.
- **HRSC2016**: high-resolution ship dataset, research license. Document.
- **AIS Trajectory anomaly synthetic dataset:** generate from Kystverket replay using documented anomaly injection rules in `ml/datasets/synthesize_anomalies.py`. This avoids any privacy or license issue.

---

## 5. Build order (do NOT skip steps)

Each phase has a definition of done. Do not start phase N+1 until phase N's checklist is green.

### Phase 0 — Foundation (Day 1)

Goal: anyone can clone the repo and run `make up` to bring up the entire local stack with placeholder services.

Tasks:
1. Initialize Git repo. Add `.gitignore` (Rust, .NET, Python, Node, IDE, OS).
2. Add `LICENSE` (MIT).
3. Add `.editorconfig` enforcing UTF-8, LF, 4-space indent for code, 2-space for YAML/JSON/Markdown.
4. Write `docker-compose.yml` with Postgres+PostGIS, Redis, MinIO, Ollama, and one stub service per backend (placeholder containers that just print "ready").
5. Write top-level `Makefile` with targets: `up`, `down`, `logs`, `test`, `lint`, `format`, `seed`, `clean`.
6. Write `README.md` (this document, slimmed for end users — keep this spec doc separately as `docs/SPEC.md`).
7. Create `.env.example` with every variable documented.
8. CI: a single GitHub Actions workflow that runs `docker compose config` to validate compose files. No actual builds yet.
9. Create the `docs/architecture.md` skeleton with a placeholder mermaid diagram.
10. Add a top-level `DISCLAIMER.md`: independent project, not affiliated with any organization, not for operational use.

Definition of done:
- [ ] `git clone && make up` works on a clean machine with only Docker installed.
- [ ] `docker compose ps` shows all stub services healthy.
- [ ] CI passes.

### Phase 1 — AIS ingestion (Days 2–6)

Goal: live vessel data flowing into Postgres, visible via a basic SQL query.

Tasks:
1. Postgres schema: `vessels` (mmsi, name, type, dimensions, last_seen), `positions` (mmsi, geom, speed, heading, timestamp), `vessel_tracks` (materialized view of recent tracks). Use migrations via **Flyway** or **golang-migrate** running as an init container.
2. Rust service:
   - Connect to `153.44.253.27:5631` over TCP (with reconnect/backoff).
   - Parse NMEA AIVDM/AIVDO sentences. Use the `ais` crate (or implement a minimal decoder for message types 1, 2, 3, 5, 18, 19, 24).
   - Upsert vessels and insert positions into Postgres using `sqlx` with batched transactions.
   - Publish each decoded message to Redis Stream `ais:positions`.
   - Expose `/healthz` and `/metrics` (Prometheus) over HTTP.
3. Replay mode: a CLI flag that reads from a recorded NMEA file instead of the live socket. Record 24 hours of live data into a fixture file checked into Git LFS or stored in MinIO seed data. Tests and CI use replay mode, never live socket.
4. Tests: parse known NMEA fixtures, assert decoded fields. Use `wiremock` to fake the TCP source.
5. Add the service to `docker-compose.yml`. Add metrics scrape config to Prometheus.

Definition of done:
- [ ] After 5 minutes of replay, `SELECT COUNT(DISTINCT mmsi) FROM positions;` returns > 100.
- [ ] Reconnect works: kill the source, restart, verify ingestion resumes without data loss for that window.
- [ ] `cargo test` green. `cargo clippy -- -D warnings` clean.

### Phase 2 — Core API and basic web map (Days 7–11)

Goal: a Blazor map showing live vessels on a Leaflet layer, updating in real time.

Tasks:
1. Core API endpoints:
   - `GET /vessels?bbox=&types=` — vessels in a bounding box, optionally filtered.
   - `GET /vessels/{mmsi}` — single vessel detail.
   - `GET /vessels/{mmsi}/track?from=&to=` — historical track as GeoJSON LineString.
   - `GET /healthz`, `/readyz`, `/metrics`.
2. SignalR hub `/hubs/vessels` that subscribes to Redis Stream `ais:positions` and pushes to clients in their current viewport.
3. Blazor WebAssembly app:
   - Leaflet map with OpenSeaMap tiles overlay.
   - Vessel markers colored by type (cargo, tanker, fishing, passenger, other).
   - Click vessel → side panel with details and 24h track.
   - SignalR connection auto-reconnects.
4. Backpressure: server-side viewport filtering so a user zoomed out doesn't get every vessel at full rate. Sample-based throttling (1 update per vessel per 3 seconds when zoomed out).
5. Auth: skip user auth for v1. Add a single API key for write endpoints when they appear in later phases.

Definition of done:
- [ ] Open the web app, see vessels move in real time.
- [ ] Click a vessel, see a 24h track drawn on the map.
- [ ] Lighthouse performance score > 80 on the map page.
- [ ] xUnit tests cover the bbox query, the SignalR hub subscription logic, and the GeoJSON serialization.

### Phase 3 — Anomaly detection (Days 12–17)

Goal: an "Anomalies" tab listing vessels with unusual recent behavior, with explanations.

Tasks:
1. Feature engineering (Python): for each vessel, compute over the last 6 hours:
   - Mean and std of speed.
   - Number of heading reversals.
   - Total stop duration (speed < 0.5 knots).
   - Mean distance from nearest coastline.
   - Trajectory entropy (binned heading distribution).
   - Time since last AIS message.
2. Isolation Forest trained on a baseline week of "normal" Norwegian traffic. Pickle to `models/isoforest.pkl`, register in MLflow.
3. LSTM autoencoder: input is a 64-step resampled trajectory (lat, lon, speed, heading), output is reconstruction error. Train with synthetic anomalies injected. Save to ONNX, register in MLflow.
4. Ensemble FastAPI service: `POST /score` with a vessel ID and time window, returns `{ score, contributing_features, model_versions }`.
5. Background worker scores every active vessel every 10 minutes, writes results to `vessel_anomalies` table.
6. Core API: `GET /anomalies?since=&min_score=` endpoint.
7. UI: anomalies tab with sortable list, click → focus map on vessel and replay the suspicious window.

Definition of done:
- [ ] Synthetic anomaly injection test: 95% of injected anomalies score above the 90th percentile of normal traffic.
- [ ] MLflow UI shows both models with metrics.
- [ ] UI lists at least 5 plausible anomalies on a 24h replay of real data.

### Phase 4 — Dark vessel detection (Days 18–23)

Goal: an overlay on the map showing SAR-detected ships and highlighting any with no matching AIS broadcast in a 30-minute window.

Tasks:
1. SAR fetcher service: scheduled job downloads new Sentinel-1 GRD scenes covering the Norwegian coast, tiles them, stores tiles in MinIO. Skip duplicates by scene ID.
2. Ship detection service: YOLOv8 trained on a ship-detection dataset, exported to ONNX. Endpoint `POST /detect` accepts a tile path, returns bounding boxes with lat/lon corners in WGS84 (using the tile's geotransform).
3. Correlation worker: for each detection, query AIS positions within a configurable spatial-temporal window (default 500m / 30min). Mark detections without matches as `dark = true` in `sar_detections` table.
4. UI: toggleable "Dark vessels" map layer showing SAR detections (small ship icons), red outline if dark, blue if matched to AIS.
5. Be honest about limitations: false positives from rocks, oil platforms, weather. Document in `docs/dark-vessel-limitations.md`. Show confidence and matched-AIS distance in the UI tooltip.

Definition of done:
- [ ] On a known scene with ground truth (use a published example), F1 score > 0.7 for ship detection.
- [ ] At least one credible "dark vessel" appears in a real Sentinel-1 scene replay; investigation shows it is plausibly a small fishing vessel below the AIS reporting threshold.

### Phase 5 — LLM agent (Days 24–28)

Goal: a chat panel where natural language questions are answered by the agent calling structured tools over the data.

Tasks:
1. Document corpus for RAG:
   - Norwegian Maritime Authority (Sjøfartsdirektoratet) public regulations, scraped responsibly with caching.
   - Kystverket AIS access policy.
   - Definitions of vessel types, AIS message types, common anomalies.
   - All embedded with `multilingual-e5-large` and stored in pgvector.
2. Semantic Kernel setup in `FjordWatch.Agent` with these tools:
   - `nearest_vessels(lat, lon, radius_km)` — calls Core API.
   - `vessel_history(mmsi, hours)` — returns trajectory summary.
   - `recent_anomalies(area_geojson, min_score)` — calls anomaly service.
   - `dark_vessels(area_geojson, since)` — calls correlation table.
   - `search_regulations(query)` — RAG over the embedded corpus.
3. Provider abstraction: `IChatProvider` with `OllamaChatProvider` (default) and `AzureOpenAIChatProvider` (toggle). Switch via `LLM_PROVIDER` env var.
4. Citation discipline: every fact in an answer must reference either a tool result (with parameters) or a document chunk (with chunk ID). The UI surfaces citations as expandable.
5. Hallucination guardrails:
   - System prompt explicitly forbids fabricating MMSI, vessel names, or coordinates.
   - If a tool returns no results, the agent says so plainly. Never invents.
   - Add an evaluation script with 30 fixed Q-A pairs; CI checks regression.
6. UI chat panel:
   - Bottom-right collapsible panel.
   - Each agent message renders citations inline.
   - "Show on map" buttons for any vessel/area mentioned in the answer.

Definition of done:
- [ ] Eval suite ≥ 80% pass.
- [ ] Manual demo questions all work cleanly:
  - "Show me cargo ships in the Oslofjord right now."
  - "Has any vessel near Bodø shown anomalous behavior in the last 6 hours?"
  - "Are there any dark vessel detections in northern Norway today?"
  - "What does Norwegian regulation say about AIS reporting requirements for fishing vessels?"
- [ ] Switching `LLM_PROVIDER=ollama` to `LLM_PROVIDER=azure_openai` works without code changes.

### Phase 6 — Polish, observability, docs (Days 29–32)

Tasks:
1. OpenTelemetry traces across all services. Verify a single request from UI through API → agent → tool → ML service → DB shows up as one trace in Tempo.
2. Grafana dashboards: ingestion rate, lag, anomaly score distribution, agent latency by tool.
3. Architecture diagram in `docs/architecture.md`, generated from a mermaid source kept in version control.
4. ADRs for every non-obvious choice (Rust, PostGIS over TimescaleDB, Semantic Kernel over LangChain, Ollama default, etc.).
5. Demo script: a 3-minute walkthrough that hits every feature. Recorded as `docs/demo.md` with timestamps; record an actual screen capture and link it.
6. Top-level README rewritten for *visitors* (the developer's hiring audience): hero screenshot, tech stack badges, quickstart, architecture diagram, link to spec.
7. CONTRIBUTING.md, even though contributions are unlikely. It signals professionalism.
8. Make sure every container has a HEALTHCHECK and a non-root user.

### Phase 7 (optional) — Cloud deployment (Days 33–35)

Tasks:
1. Bicep templates for: Resource Group, Container Apps Environment, Postgres Flexible Server with PostGIS extension enabled, Storage Account, Container Registry, Application Insights, Key Vault.
2. GitHub Actions workflow that builds images, pushes to ACR, and deploys to Container Apps on push to `main`.
3. A `make deploy` target that runs the whole flow with sane defaults.
4. Cost estimate documented in `docs/deployment.md`. Default to scale-to-zero where possible. The cloud path must not be required to run the project.

---

## 6. Quality bar

These standards apply to all code. Treat them as merge-blocking.

- **Linting/formatting:** `cargo fmt && cargo clippy -- -D warnings`, `dotnet format --verify-no-changes`, `ruff check && ruff format --check`, `dotnet format whitespace` for Razor, `eslint && prettier --check` for the e2e tests.
- **Type safety:** Python uses strict `mypy` settings or `pyright` in strict mode. Treat ignored errors as bugs.
- **Tests:** new code has tests. Coverage target ≥ 70% lines, ≥ 60% branches. Coverage gate in CI.
- **Commit hygiene:** Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`). Commits are small and atomic. Squash before merge if needed.
- **PR template:** what changed, why, how tested, screenshots/GIFs for UI changes.
- **Secrets:** none in repo. `.env.example` only.
- **Logging:** structured (JSON) logs with correlation IDs. No `Console.WriteLine`, `print`, or `println!` in committed code.
- **Documentation:** every service has a `README.md` with what/why/run/test sections. Public APIs have OpenAPI specs published from code.

---

## 7. How to work on this project (instructions to Claude Code)

You are building this incrementally. Follow these working rules:

1. **Read the whole spec once before starting.** Then start at Phase 0.
2. **Confirm the plan for each phase** before writing code. Post a short message stating: the phase, the files you will create or modify, and any deviations from the spec with justification. Wait for the developer to confirm or override.
3. **One phase at a time.** Do not skip ahead. Do not start phase N+1 before phase N's checklist is green.
4. **Run the checks yourself.** After each phase, run the linters, the tests, and the relevant `make` targets. Paste the output. Fix anything red.
5. **Commit at meaningful units of work**, not at end of phase. Use Conventional Commits.
6. **When you make a non-trivial decision, write an ADR** in `docs/adr/`. Number them sequentially.
7. **Ask before introducing dependencies.** Each new crate, NuGet package, or PyPI package should be justified in the PR description and added to the relevant ADR if it is core.
8. **Be honest about uncertainty.** If you don't know how Kystverket's TCP framing handles partial reads, say so and write a probe before assuming.
9. **Prefer boring solutions.** This project gets value from being correct and well-engineered, not from being clever.
10. **Stop and ask if you find yourself reframing the spec.** If you think the spec is wrong, push back explicitly with reasoning. Don't silently rework.

---

## 8. What "done" looks like

The project is done when all of the following are true:

- [ ] A new developer can run `git clone <repo> && cp .env.example .env && make up` and within 5 minutes have the full stack running locally with live (or replayed) AIS data flowing.
- [ ] The web app shows live vessels, anomaly list, dark vessels overlay, and a working LLM chat panel.
- [ ] All CI workflows are green on `main`.
- [ ] All ADRs are written.
- [ ] A 3-minute demo video is recorded and linked from the README.
- [ ] The README has hero screenshots, an architecture diagram, and a clear quickstart.
- [ ] Cloud deployment Bicep is present and tested at least once (Phase 7).
- [ ] The repo is public on GitHub with topics: `maritime`, `ais`, `dotnet`, `rust`, `blazor`, `machine-learning`, `llm-agent`, `norway`, `geospatial`, `dark-vessel-detection`.

---

## 9. About the developer (context for the agent)

The developer (Erdem Kilci) is a Test Developer at TOMRA, a master's student in Informatics at UiO, and a bachelor's graduate in Data Engineering from OsloMet. Strong in C#/.NET (Blazor, ASP.NET Core, EF Core), Python (ML, scikit-learn, PyTorch basics), C++ (embedded), Docker, Azure (AI-900, AI-102 certified). Comfortable with Linux, Jenkins, Robot Framework, Jira. Norwegian Bokmål and English fluent. Currently building this project to land roles at Norwegian companies in AI/ML, fullstack, .NET, system engineering, and defense/maritime tech.

When proposing tradeoffs, default toward choices that:
- Demonstrate breadth (Rust + .NET + Python is good).
- Land near Norwegian employer stacks (Azure, Microsoft ecosystem, Postgres, Kubernetes/Container Apps).
- Are visibly production-grade (observability, tests, CI, ADRs).

---

## 10. Disclaimer

This is an independent open-source educational and portfolio project. It is not affiliated with, endorsed by, or representing any company or organization, including but not limited to TOMRA ASA, Kongsberg Gruppen, DNV, Cognite, Equinor, Kystverket, or the Norwegian government. Data from public sources is used under their respective open licenses. The system is not intended for, and must not be used for, operational maritime surveillance, law enforcement, military targeting, or any decision affecting real-world safety. The authors accept no liability for any use of this software.

---

*End of spec. Claude Code: when you have read this fully, reply with: "Spec read. Starting Phase 0. Plan: [...]" and wait for confirmation.*
