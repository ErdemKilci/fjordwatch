-- V4: pgvector extension + RAG corpus tables for the FjordWatch agent.
--
-- The corpus is hand-curated (Sjøfartsdirektoratet regulations, Kystverket
-- AIS access policy, ITU-R M.1371-5 ship-type definitions, internal glossary).
-- The ingestion script writes one row per ~700-token chunk with its
-- embedding from `intfloat/multilingual-e5-large` (or compatible).

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS regulation_chunks (
    id            BIGSERIAL PRIMARY KEY,
    source        TEXT        NOT NULL,    -- canonical URL
    title         TEXT        NOT NULL,
    chunk_index   INTEGER     NOT NULL,
    text          TEXT        NOT NULL,
    embedding     vector(1024) NOT NULL,
    language      TEXT        NOT NULL DEFAULT 'no',
    fetched_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (source, chunk_index)
);

CREATE INDEX IF NOT EXISTS regulation_chunks_embedding_idx
    ON regulation_chunks USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);

CREATE INDEX IF NOT EXISTS regulation_chunks_source_idx
    ON regulation_chunks (source);

-- Eval-run history. Populated by `dotnet test --filter Category=Eval` so a
-- developer can track quality regressions across model swaps.
CREATE TABLE IF NOT EXISTS agent_eval_runs (
    id              BIGSERIAL PRIMARY KEY,
    run_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    provider        TEXT        NOT NULL,
    model           TEXT        NOT NULL,
    questions_total INTEGER     NOT NULL,
    passed          INTEGER     NOT NULL,
    score           REAL        NOT NULL,
    notes           TEXT
);

COMMENT ON COLUMN regulation_chunks.embedding IS
    'Cosine-normalized embedding vector. Dimension matches multilingual-e5-large (1024).';
COMMENT ON COLUMN regulation_chunks.language IS
    'ISO 639-1 language code; majority Norwegian (no), some English (en).';
