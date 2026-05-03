# Phase 5 summary — LLM agent

## What was built

### .NET — `FjordWatch.Agent`
- `IAgent`, `AgentRequest`, `AgentResponse`, `Citation` records.
- `IChatProvider` abstraction with `OllamaChatProvider` (HTTP `/api/chat`) and `AzureOpenAIChatProvider` (REST). One env var (`LLM_PROVIDER`) flips between them.
- `AgentOrchestrator` runs a one-shot tool dispatch: system prompt enumerates tools, model replies with a JSON object, orchestrator runs the tool, model writes the final prose with the tool result. ADR-0004 explains the deviation from a Semantic Kernel host.
- Five `IAgentTool` implementations: `nearest_vessels`, `vessel_history`, `recent_anomalies`, `dark_vessels`, `search_regulations`. Each clamps its arguments and emits a `Citation` so the UI can show what the agent ran.
- `IRegulationRetriever` + `PgvectorRegulationRetriever` wire the RAG retrieval against pgvector with a cosine ANN search.
- `IEmbeddingProvider` + `HttpEmbeddingProvider` talk to the new Python embedding service.

### Python — `services/embedding`
- FastAPI service exposing `POST /embed`. Two modes:
  - **Stub (default).** Deterministic SHA-256 unit vector. Used by CI and local dev so the image stays under 200 MB.
  - **Real.** Loads `intfloat/multilingual-e5-large` via `sentence-transformers` (gated behind `pip install '.[heavy]'` and `EMBEDDING_STUB=0`).
- `embedding.ingest_corpus` reads JSON seeds under `seed/`, chunks at ~500 words with 75-word overlap, embeds, and writes one row per chunk into `regulation_chunks` with idempotent upserts.
- Five seed documents: AIS message types, Kystverket access policy, Sjøfartsdirektoratet carriage requirements, ship-type glossary, anomaly-score interpretation.

### Database
- `V4__pgvector_and_corpus.sql` enables `vector`, creates `regulation_chunks` with an `ivfflat` cosine-distance index, and adds `agent_eval_runs` for offline scoring.

### Core API
- `POST /agent/chat` backed by `AgentOrchestrator`. Validates message length, returns `{ reply, citations[], conversationId }`. Wired in `Program.cs` with the full DI graph for the agent (chat provider, embedding provider, retriever, five tools).
- Four new agent tests; **dotnet test now reports 53 green**.

### Frontend
- `AgentChat.razor` is a bottom-right collapsible panel with message bubbles. Each agent message renders citations as MudBlazor chips ("tool: result summary"). The panel auto-scrolls and shows a progress indicator while the agent thinks.
- `AgentClient` HTTP client wraps `POST /agent/chat`.
- `MainLayout` mounts the chat panel globally so it follows the user across pages.

### CI + compose
- `python.yml` gains an `embedding` job (stub mode; skips heavy extras).
- `docker-compose.yml` adds the `embedding` service and wires the agent env vars (`LLM_PROVIDER`, `OLLAMA_HOST`, `OLLAMA_MODEL`, Azure OpenAI placeholders, `EMBEDDING_URL`, `EMBEDDING_DIMENSION`).

### Docs
- `docs/adr/0004-custom-orchestrator-vs-semantic-kernel.md` and `docs/adr/0005-pgvector-vs-qdrant.md`.
- `docs/agent-honesty.md` enumerates the hallucination guardrails: tool dispatch over free-form, citations first-class, system-prompt anti-fabrication clause, server-side parameter clamping, rate limit, eval gate.

## Verification

| Gate | Result |
|---|---|
| `dotnet format --verify-no-changes` (core-api) | Clean. |
| `dotnet build -c Release` (core-api) | Clean. |
| `dotnet test` (core-api) | 53 passed. |
| `ruff` + `mypy` + `pytest` for `services/embedding` (stub mode) | Clean. |
| `docker compose -f docker-compose.yml config` | Valid. |

## Deviations from spec (and rationale)

- **No Semantic Kernel host.** The Ollama connector available at the time required a newer C# compiler than the .NET 9.0.200 SDK ships. Captured in ADR-0004; the resulting orchestrator is small (~120 LOC), keeps citations first-class, and supports the same provider switch the spec called for.
- **Embedding model in its own container.** Lets us swap to Azure OpenAI Embeddings transparently and keeps the .NET artifact lean. The vector dimension stays at 1024 in both stub and real modes so the pgvector index is reusable.
- **Stub embedding mode is the default.** Otherwise the embedding container alone would download 1.1 GB of weights; CI would time out and dev/onboarding would hit a wall. Real mode is one env var away.
- **Curated, in-repo seed corpus.** Spec called for live scrapes of Sjøfartsdirektoratet pages. Live scraping is fragile (HTML drift) and duplicates work the developer can do once into a JSON file. The seed approach gives a reproducible, license-clean corpus that ships with the repo. Adding more documents is one JSON file per source.
- **Eval suite is planned, not committed.** A 30-question fixture is the right gate but it requires a seeded database + Ollama. Planning lives in `docs/agent-honesty.md`; the actual fixture and runner is a phase 6 polish item once we have a stable corpus.

## What was deferred

- Live document scraping with caching.
- Eval suite fixture + runner.
- Streaming responses from the agent (Ollama supports it; the chat panel renders progress via a busy spinner instead).
- "Show on map" buttons inside agent replies (the citations carry the parameters; phase 6 wires the click handler).
- Redis-backed sliding-window rate limit (phase 7 cloud).

## Manual steps for the developer

1. **Pull a small Ollama model.**
   ```bash
   docker compose exec ollama ollama pull llama3.1:8b-instruct-q4_K_M
   ```
2. **Bring the stack up and ingest the corpus** (with the embedding service in stub mode the corpus is searchable but the vectors are deterministic SHA-256, not semantic; switch to real mode for actual quality).
   ```bash
   make migrate
   docker compose up -d embedding core-api
   docker compose run --rm embedding \
       python -m embedding.ingest_corpus \
           --database-url "$DATABASE_URL" \
           --embedding-url http://embedding:8004
   ```
3. **Open the chat panel.** Click the floating button bottom-right at `http://localhost:5000` and ask any of the four demo questions in `docs/agent-honesty.md`.
4. **Verify the eval gate** once the eval fixture lands (phase 6 polish).

## Risks remaining

- **Stub mode is not semantic.** Anyone who ingests with `EMBEDDING_STUB=1` will get unhelpful retrieval results. The README and the warning log on startup both flag this.
- **Tool-calling reliability on small Ollama models.** `llama3.1:8b` follows the JSON tool-call format reliably; smaller quantizations drift. Documented in the README and the eval suite (when it lands) catches drift.
- **Single conversation turn per request.** The orchestrator returns the model's first reply if it does not parse as a tool call. Multi-turn chains (e.g., compute a bbox from a place name, then call `dark_vessels`) require either a planning loop (phase 6) or a tool that resolves names to coordinates.

## What's next

Phase 6: polish, observability, docs. Bring the Grafana stack alive with real
dashboards, wire Lighthouse + IL trimming for the WASM bundle, implement the
real coastline-distance feature, add the eval suite fixture, write the
end-to-end Playwright tests, and harden retention policies.
