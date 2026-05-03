from __future__ import annotations

import os
from collections.abc import Iterator

import pytest

# Ensure pydantic-settings can construct a Settings() without a real
# Copernicus account during test collection.
os.environ.setdefault("S3_ENDPOINT", "http://minio:9000")


@pytest.fixture(autouse=True)
def _reset_settings_singleton() -> Iterator[None]:
    from sar_fetcher import config

    config._settings = None
    yield
    config._settings = None
