# 0005. pgvector for the RAG corpus, not a separate vector database

- **Status:** Accepted
- **Date:** 2026-05-03
- **Deciders:** Erdem Kilci

## Context

The agent's RAG retriever needs an approximate-nearest-neighbour index over
~50 chunks at first and at most a few thousand once the corpus grows. The
data is co-located with the operational Postgres + PostGIS instance the rest
of the stack already uses.

## Decision

Use the **`pgvector`** extension on the existing Postgres instance, with an
`ivfflat` index (`lists = 100`) over a 1024-dimensional cosine-normalized
embedding column on `regulation_chunks`.

## Considered alternatives

- **Qdrant.** Pros: purpose-built ANN, gRPC API, payload filters, hybrid search. Cons: another container, another datastore to back up, another set of credentials, separate operational story. Overkill for a corpus measured in the hundreds of chunks.
- **Weaviate, Milvus, Chroma.** Same trade-offs as Qdrant; pgvector is enough.
- **In-memory FAISS.** Cons: lost on container restart, no concurrent writers, awkward for the .NET reader.

## Consequences

- **Positive:** one database to back up; pgvector composes naturally with PostGIS spatial filters and JOINs against `vessels`/`positions`; no new operational surface.
- **Negative:** ivfflat at the default `lists` is a coarse index; recall@k for very large corpora drops. Mitigation: revisit at ~10k chunks (phase 6 polish) and switch to `hnsw` which pgvector now supports.
- **Follow-ups:** add a recall benchmark to the eval harness so we notice if recall regresses with corpus growth.

## References

- pgvector: https://github.com/pgvector/pgvector
- "When to use a vector database" (Postgres FM): https://postgres.fm/episodes/pgvector
