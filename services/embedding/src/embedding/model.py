"""Embedding model wrapper.

Two modes:
  * **Real:** loads ``intfloat/multilingual-e5-large`` via sentence-transformers.
    Requires ``pip install .[heavy]`` and downloads ~1.1 GB of weights.
  * **Stub (``EMBEDDING_STUB=1``):** returns a deterministic random vector
    derived from a SHA-256 of the input. Vector norm is 1.0 so it is
    cosine-comparable. Used by CI and unit tests to keep image size sane
    and avoid model downloads.
"""

from __future__ import annotations

import hashlib
import logging
from collections.abc import Callable

import numpy as np

logger = logging.getLogger(__name__)


class EmbeddingModel:
    def __init__(self, *, dimension: int, encode_fn: Callable[[str], np.ndarray]) -> None:
        self.dimension = dimension
        self._encode_fn = encode_fn

    def embed(self, text: str) -> np.ndarray:
        if not isinstance(text, str) or not text.strip():
            raise ValueError("text must be a non-empty string")
        return self._encode_fn(text)


def load_real_model(name: str, dimension: int) -> EmbeddingModel:
    from sentence_transformers import SentenceTransformer

    model = SentenceTransformer(name)

    def _encode(text: str) -> np.ndarray:
        # E5 expects "query: " or "passage: " prefixes; the agent sends queries
        # so we wrap accordingly.
        prefixed = "query: " + text
        vec = model.encode(prefixed, normalize_embeddings=True)
        return np.asarray(vec, dtype=np.float32)

    logger.info("loaded embedding model %s (dim %d)", name, dimension)
    return EmbeddingModel(dimension=dimension, encode_fn=_encode)


def load_stub_model(dimension: int) -> EmbeddingModel:
    def _encode(text: str) -> np.ndarray:
        digest = hashlib.sha256(text.encode("utf-8")).digest()
        # Repeat the digest to fill ``dimension`` bytes deterministically.
        repeats = -(-dimension // len(digest))
        seed_bytes = (digest * repeats)[:dimension]
        arr = np.frombuffer(seed_bytes, dtype=np.uint8).astype(np.float32)
        arr = arr / 255.0 - 0.5
        norm = float(np.linalg.norm(arr))
        denom = norm if norm > 1e-9 else 1e-9
        return (arr / denom).astype(np.float32)

    logger.warning("using stub embedding model (dim %d)", dimension)
    return EmbeddingModel(dimension=dimension, encode_fn=_encode)
