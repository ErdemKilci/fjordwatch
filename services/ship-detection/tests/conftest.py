from __future__ import annotations

import os
from collections.abc import Iterator

import pytest

# Ensure the Settings singleton sees a non-empty DATABASE_URL during test
# collection (the api module's lifespan calls get_settings()).
os.environ.setdefault("DATABASE_URL", "postgres://x:x@localhost:5432/x")


@pytest.fixture(autouse=True)
def _reset_settings_singleton() -> Iterator[None]:
    from ship_detection import config

    config._settings = None
    yield
    config._settings = None
