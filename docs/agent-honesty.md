# Agent honesty — how FjordWatch tries not to hallucinate

The FjordWatch agent answers natural-language questions about Norwegian
maritime traffic by calling tools over the FjordWatch data and a small RAG
corpus. The risk of an LLM inventing MMSIs, vessel names, or coordinates is
the single biggest correctness risk in this project. This document records
the guardrails and how to verify them.

## Guardrails

1. **Tool dispatch, not free-form answer.** The system prompt instructs the
   model to reply with a single JSON object naming a tool and its arguments.
   The orchestrator parses that JSON, runs the tool, and only then asks the
   model to write the final prose. The model never sees vessel data
   without first triggering a deterministic, server-side query.

2. **Citations are first-class.** Every tool returns a `Citation` object
   carrying the tool name, the parameters used, and a result summary. The
   API surfaces the citations alongside the reply; the UI renders them as
   chips below each answer. Reviewers can inspect the parameters to confirm
   the agent did not silently substitute different values.

3. **System prompt forbids fabrication.** Excerpt: "Never invent or guess
   MMSI numbers, vessel names, coordinates, dates, or scores. If a tool
   returns no results, say so plainly."

4. **Server-side parameter validation.** Every tool clamps its arguments
   (radius capped at 200 km, hours capped at 168, scores clamped to
   `[0, 1]`, bounding boxes rejected if invalid) before hitting Postgres.
   The model cannot trick a tool into returning more than the configured
   limit by passing an over-large parameter.

5. **Rate limit.** The `/agent/chat` endpoint accepts at most 10 requests
   per minute per IP. A tab left open cannot drain Ollama, and a malicious
   client cannot use the agent as a free-form Postgres proxy.

6. **Eval suite gate.** A 30-question fixture (planned: lives in
   `services/core-api/FjordWatch.Agent.Tests/Eval/eval_questions.json`)
   covers the canonical demo questions and known-unanswerable variations
   ("show me the vessel `123456789` that does not exist"). The eval runs
   manually before tagging a release; the gate is ≥ 80% pass.

## What the agent will refuse to do

- Answer questions about specific people, operators, or owners. The vessel
  data only carries MMSIs and vessel names; mapping to a person is outside
  scope.
- Claim that a vessel is doing anything illegal, suspicious, or worth
  reporting. Anomaly scores are statistical, not evidentiary; "dark vessel"
  detections include rocks and oil platforms (see
  [`dark-vessel-limitations.md`](dark-vessel-limitations.md)).
- Make up coordinates, MMSIs, or names. If a tool returns an empty result
  set, the agent must say so plainly.

## Manual demo questions

Each of these works against a seeded local stack with Ollama running:

1. *Show me cargo ships in the Oslofjord right now.*
2. *Has any vessel near Bodø shown anomalous behavior in the last 6 hours?*
3. *Are there any dark vessel detections in northern Norway today?*
4. *What does Norwegian regulation say about AIS reporting requirements for fishing vessels?*

For each, the answer must include at least one citation and must reference
real values from the database, not invented ones.

## Verification

- `POST /agent/chat` with `{"message": "show me cargo ships near Oslo"}` returns a JSON body whose `citations` array is non-empty.
- Citations on a "regulations" answer point at `search_regulations` with the user's query in the parameters.
- The reply quotes a regulation excerpt and cites the chunk's source URL or `internal://...` identifier.
- Asking about a fabricated MMSI produces an answer that says no positions exist for that MMSI, not an invented track.
