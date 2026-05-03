from __future__ import annotations

import os
from collections.abc import Iterator

import pytest

os.environ.setdefault("EMBEDDING_STUB", "1")


@pytest.fixture(autouse=True)
def _reset_settings_singleton() -> Iterator[None]:
    from embedding import config

    config._settings = None
    yield
    config._settings = None
