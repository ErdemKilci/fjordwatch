# Phase 5 plan — LLM agent

## Goal
A chat panel in the bottom-right of the Blazor app where natural-language questions are answered by a Semantic-Kernel-driven agent that calls structured tools over the FjordWatch data and a small RAG corpus. Default LLM is Ollama; Azure OpenAI is a one-env-var toggle. Every fact in an answer cites either a tool result or a document chunk; the agent refuses to fabricate MMSIs, names, or coordinates.

## Files to create

### Database
1. `services/db/migrations/V4__pgvector_and_corpus.sql` — `CREATE EXTENSION pgvector`, `regulation_chunks` table (`id`, `source`, `title`, `chunk_index`, `text`, `embedding vector(1024)`, `language`, `fetched_at`), `agent_eval_runs` table (run-level metrics for the offline suite).

### .NET 9 — `FjordWatch.Agent`
2. `IAgent.cs` and `AgentResponse.cs` in `FjordWatch.Agent` — request/response shapes including a `Citation[]` list.
3. `IChatProvider.cs` interface (sync over messages and tool calls), with `OllamaChatProvider` (HTTP against `OLLAMA_HOST`) and `AzureOpenAIChatProvider` (Azure OpenAI Chat Completions). Selection by `LLM_PROVIDER` env var.
4. `KernelFactory.cs` — Semantic Kernel kernel construction with the five tools registered.
5. `Tools/NearestVesselsTool.cs`, `Tools/VesselHistoryTool.cs`, `Tools/RecentAnomaliesTool.cs`, `Tools/DarkVesselsTool.cs`, `Tools/SearchRegulationsTool.cs` — each a thin wrapper around an existing repository or HTTP client; each emits a `Citation` (tool name, parameters, row counts).
6. `Embedding/IEmbeddingProvider.cs` + `LocalE5EmbeddingProvider.cs` (HTTP against a separate `embedding-service` we keep at parity with the existing Python pattern), `AzureEmbeddingProvider.cs` for the cloud path.
7. `Rag/RegulationRetriever.cs` — pgvector ANN search via Dapper.
8. `FjordWatch.Agent.Tests/` — unit tests on tool result shaping, citation accumulation, Ollama client serialization, and a deterministic-fake `IChatProvider` that lets us write end-to-end tests without a live LLM.

### .NET 9 — `FjordWatch.Api`
9. `Endpoints/AgentEndpoints.cs` — `POST /agent/chat`. Request: `{ message, conversation_id? }`. Response: `{ reply, citations[], conversation_id }`.
10. `RateLimit/AgentRateLimiter.cs` — small fixed-window limiter (10 req/min/IP by default) so a tab left open can't drain Ollama. Returns 429 with `Retry-After`.

### Embedding service (`services/embedding/`, Python 3.12, FastAPI)
11. `pyproject.toml`, `src/embedding/main.py`, `src/embedding/api.py`, `src/embedding/model.py` (loads `intfloat/multilingual-e5-large` at startup; ships a "stub" mode that returns deterministic random vectors when `EMBEDDING_STUB=1` so CI doesn't pull a 1.1GB model).
12. `tests/`, `Dockerfile`, `README.md`.

### RAG ingestion (`services/embedding/scripts/`)
13. `ingest_corpus.py` — fetches a fixed list of public Norwegian maritime documents (Sjøfartsdirektoratet PDFs, Kystverket AIS access policy, ITU-R M.1371-5 ship-type table), chunks at ~700 tokens with 100-token overlap, calls the embedding service, writes to Postgres. Caches HTTP responses under `seed/` so re-runs are offline. `--seed-from-fixtures` mode bundles a curated set under `services/embedding/seed/` for CI.

### Frontend
14. `services/web/FjordWatch.Web/Components/AgentChat.razor` — collapsible panel, message bubbles with citation chips, "Show on map" buttons that fire focus events.
15. `wwwroot/js/leaflet-interop.js` — small additions for "focus-on-vessel" / "focus-on-area" without changing the existing layers.
16. `Services/AgentClient.cs` — HTTP client for `/agent/chat`.

### CI + compose
17. `.github/workflows/python.yml` — add an `embedding` job (skip the model download via `EMBEDDING_STUB=1`).
18. `docker-compose.yml` — add `embedding` service, env var pass-through for `LLM_PROVIDER`, Azure OpenAI vars.

### Docs
19. `docs/adr/0004-semantic-kernel-and-ollama-default.md` — the SK + Ollama choice with the Azure-OpenAI toggle path.
20. `docs/adr/0005-pgvector-vs-qdrant.md` — record using pgvector (single-database story) over a separate Qdrant deployment.
21. `docs/agent-honesty.md` — the "no fabrications" guardrails: system prompt, refusal patterns, citation enforcement, evaluation gate.
22. `services/core-api/FjordWatch.Agent/README.md`.

### Eval
23. `services/core-api/FjordWatch.Agent.Tests/Eval/eval_questions.json` — 30 fixed Q-A pairs.
24. `services/core-api/FjordWatch.Agent.Tests/Eval/EvalRunner.cs` — `[Trait("Category","Eval")]` test that hits a fixed seeded Postgres + the `FakeChatProvider` and asserts on a deterministic match score. Skipped by default; run via `dotnet test --filter Category=Eval`.

## Deviations from spec

- **Embedding service is its own container, not in-process.** The spec says "embedded with `multilingual-e5-large` and stored in pgvector" without dictating where. Hosting the model in a small FastAPI service keeps the .NET build artifact-free, lets the cloud path swap to Azure OpenAI Embeddings transparently, and matches the Ollama pattern (separate process for the model). The `IEmbeddingProvider` abstraction makes both deployments trivial.
- **`mxbai-embed-large` as a fallback when the upstream e5-large fails to download.** Same vector dimension, comparable quality on Norwegian text; documented in the README.
- **Eval suite is `[Trait("Category","Eval")]` and skipped in PR CI by default.** Real eval needs a seeded database and a live (Ollama) model — neither is fast enough for PR-feedback loops. We run it manually before tagging, and document it as the phase-5 acceptance gate (≥ 80% pass).
- **Rate limit is fixed-window in-process, not Redis-backed.** Single core-api instance is the only deployment shape we have; Redis-backed sliding windows add latency for no PR-stage benefit. Phase 7 (cloud) revisits.
- **Citations are first-class in the response.** Every tool emits a `Citation` object (`source`, `parameters_json`, `result_summary`); RAG retrievals emit a `Citation` per chunk (`title`, `source_url`, `chunk_id`). The system prompt instructs the model to surface them; the API enforces non-empty `Citations` for any factual answer.
- **No tool that hits Met.no weather yet.** Spec mentions Met.no in section 4 but section 5 phase 5 doesn't list it as a tool; we keep the tool count at five.

## Verification

| Gate | How |
|---|---|
| `dotnet build`, `dotnet format`, `dotnet test` | Clean. New tests cover each tool's parameter handling, the FakeChatProvider, citation accumulation, and Ollama JSON parsing. |
| `ruff`, `mypy`, `pytest` for the embedding service | Clean. Stub mode + a real-model integration test gated behind `EMBEDDING_STUB=0`. |
| `docker compose up embedding core-api web` | Healthy. |
| Manual demo questions | Each works against a seeded database + Ollama running locally. |
| Eval suite | Manual run; gate ≥ 80%. |

## Risks

- **Ollama latency.** A small Llama variant on a CPU-only laptop is 2-5s per turn; the chat panel needs a "thinking…" affordance. We add an SSE streaming variant later (phase 6 polish).
- **Hallucination.** The biggest risk in this phase. Mitigations: (1) tools enforce parameter validation server-side; (2) the system prompt forbids fabrication and instructs the model to use exactly the tools available; (3) the API rejects any answer with zero citations on a question that contains a vessel reference, MMSI, or coordinate; (4) the eval suite catches regressions.
- **pgvector index choice.** We start with `ivfflat` (lists=100) which is the safe default; phase 6 polish revisits if recall@k drops.
- **Provider switching coverage.** Both providers must support function-calling. Ollama's function-calling is improving but not yet consistent for all small models; we document a "tested with `llama3.1:8b-instruct`" baseline in the README and let the developer override `OLLAMA_MODEL`.
