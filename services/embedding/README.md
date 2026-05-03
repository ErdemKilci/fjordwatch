# embedding (Python 3.12, FastAPI)

Tiny embedding service used by the FjordWatch agent's RAG retrieval and the
`scripts/ingest_corpus.py` ingestion pipeline.

## Modes

- **Stub (default).** Returns a deterministic SHA-256-derived unit-norm vector. Used by CI and local dev so the image stays under 200 MB.
- **Real.** Loads `intfloat/multilingual-e5-large` via `sentence-transformers`. Toggle with `EMBEDDING_STUB=0` and rebuild after `pip install '.[heavy]'`.

Vector dimension defaults to 1024 (matching e5-large) and is stable across
both modes so the pgvector index is reusable.

## Endpoints

| Method | Path | Returns |
|---|---|---|
| GET | `/healthz` | `200 ok`. |
| GET | `/readyz` | `200 ready`. |
| POST | `/embed` | `{ embedding: number[], model, dimension }` |
| GET | `/metrics` | Prometheus exposition. |

## Ingestion

```bash
docker compose run --rm embedding \
    python -m embedding.ingest_corpus \
        --database-url "$DATABASE_URL" \
        --embedding-url http://embedding:8004
```

Seed documents live as JSON files under `seed/`. They are checked into Git
so ingestion runs offline and reproducibly.

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `EMBEDDING_MODEL` | `intfloat/multilingual-e5-large` | sentence-transformers model id. |
| `EMBEDDING_DIMENSION` | `1024` | Vector dimension. |
| `EMBEDDING_STUB` | `1` | When `1`, deterministic stub. When `0`, real model. |
| `EMBEDDING_PORT` | `8004` | HTTP listen port. |
