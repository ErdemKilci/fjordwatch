"""Ingest the FjordWatch RAG corpus into Postgres.

Reads a curated set of public Norwegian maritime documents (Sjøfartsdirektoratet
regulation excerpts, Kystverket AIS access policy summary, ITU-R M.1371-5
ship-type table, an internal glossary of common AIS message types). All
inputs live as JSON files under ``services/embedding/seed/`` and are checked
into Git so the ingestion is fully offline. The script chunks each document
at ~700 tokens with 100-token overlap, calls the local embedding service,
and writes one row per chunk into ``regulation_chunks``.

Usage::

    python -m embedding.scripts.ingest_corpus \
        --database-url postgres://... \
        --embedding-url http://localhost:8004
"""

from __future__ import annotations

import argparse
import json
import logging
from pathlib import Path
from typing import Any

import httpx
import psycopg

DEFAULT_SEED_DIR = Path(__file__).resolve().parents[2] / "seed"


def chunk_text(text: str, *, target_words: int = 500, overlap_words: int = 75) -> list[str]:
    """Word-level chunker. We use words rather than tokens so the corpus
    preprocessing has zero runtime dependency on tiktoken; the embedding
    model accepts up to 512 tokens, which roughly equals 700 words for
    Norwegian/English mixed input. The 75-word overlap preserves context
    across chunk boundaries."""
    words = text.split()
    if not words:
        return []
    chunks: list[str] = []
    step = max(1, target_words - overlap_words)
    for start in range(0, len(words), step):
        end = min(len(words), start + target_words)
        chunks.append(" ".join(words[start:end]))
        if end == len(words):
            break
    return chunks


def embed_chunks(client: httpx.Client, embedding_url: str, chunks: list[str]) -> list[list[float]]:
    embeddings: list[list[float]] = []
    for chunk in chunks:
        resp = client.post(f"{embedding_url}/embed", json={"text": chunk}, timeout=60.0)
        resp.raise_for_status()
        embeddings.append(resp.json()["embedding"])
    return embeddings


def upsert_document(
    conn: psycopg.Connection,
    *,
    source: str,
    title: str,
    language: str,
    chunks: list[str],
    embeddings: list[list[float]],
) -> int:
    rows = 0
    with conn.cursor() as cur:
        for index, (text, embedding) in enumerate(zip(chunks, embeddings, strict=True)):
            embedding_literal = "[" + ",".join(f"{x}" for x in embedding) + "]"
            cur.execute(
                """
                INSERT INTO regulation_chunks (source, title, chunk_index, text, embedding, language)
                VALUES (%s, %s, %s, %s, %s::vector, %s)
                ON CONFLICT (source, chunk_index) DO UPDATE
                SET text = EXCLUDED.text,
                    embedding = EXCLUDED.embedding,
                    title = EXCLUDED.title,
                    language = EXCLUDED.language,
                    fetched_at = now()
                """,
                (source, title, index, text, embedding_literal, language),
            )
            rows += 1
    conn.commit()
    return rows


def load_seed(seed_dir: Path) -> list[dict[str, Any]]:
    docs = []
    for path in sorted(seed_dir.glob("*.json")):
        with path.open(encoding="utf-8") as f:
            docs.append(json.load(f))
    return docs


def main() -> None:
    parser = argparse.ArgumentParser(description="Ingest the FjordWatch RAG corpus")
    parser.add_argument("--database-url", required=True)
    parser.add_argument("--embedding-url", default="http://localhost:8004")
    parser.add_argument("--seed-dir", type=Path, default=DEFAULT_SEED_DIR)
    args = parser.parse_args()

    logging.basicConfig(level=logging.INFO, format="%(message)s")
    docs = load_seed(args.seed_dir)
    if not docs:
        raise SystemExit(f"no seed documents found under {args.seed_dir}")

    total_chunks = 0
    with httpx.Client() as client, psycopg.connect(args.database_url) as conn:
        for doc in docs:
            chunks = chunk_text(doc["text"])
            embeddings = embed_chunks(client, args.embedding_url, chunks)
            written = upsert_document(
                conn,
                source=doc["source"],
                title=doc["title"],
                language=doc.get("language", "no"),
                chunks=chunks,
                embeddings=embeddings,
            )
            logging.info("ingested %s -> %d chunks", doc["source"], written)
            total_chunks += written
    logging.info("done: %d chunks across %d documents", total_chunks, len(docs))


if __name__ == "__main__":
    main()
