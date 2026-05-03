from __future__ import annotations

import pytest

from sar_fetcher.config import Settings
from sar_fetcher.copernicus_client import _fixture_catalog, search_recent_scenes


@pytest.mark.asyncio
async def test_fixture_fallback_when_no_credentials() -> None:
    settings = Settings(
        S3_ENDPOINT="http://minio:9000",
        COPERNICUS_USERNAME=None,
        COPERNICUS_PASSWORD=None,
    )
    scenes = await search_recent_scenes(settings)
    assert scenes
    assert scenes[0].scene_id == "S1A_IW_GRDH_FIXTURE_LOFOTEN"


def test_fixture_catalog_has_lofoten_bbox() -> None:
    scene = _fixture_catalog()[0]
    assert scene.bbox == (12.0, 67.5, 16.0, 69.0)
