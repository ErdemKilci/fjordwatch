"""Thin client for the Copernicus Data Space Ecosystem OData/Catalogue API.

The full client supports searching, downloading, and access-token rotation.
We implement only the search step here; the actual GRD download is done by
``rasterio.open`` against the signed URL the search returns.

If the ``COPERNICUS_USERNAME`` env var is blank, :func:`search_recent_scenes`
falls back to a static fixture catalog so the dev/CI loops keep working
without a real Copernicus account.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta

import httpx

from .config import Settings

logger = logging.getLogger(__name__)

CATALOG_BASE = "https://catalogue.dataspace.copernicus.eu/odata/v1/Products"


@dataclass(frozen=True)
class SceneMetadata:
    scene_id: str
    name: str
    sensing_start: datetime
    sensing_end: datetime
    download_url: str
    bbox: tuple[float, float, float, float]


def _fixture_catalog() -> list[SceneMetadata]:
    """Stable fallback catalog when no Copernicus credentials are configured.

    Returns one fictional scene roughly over the Lofoten archipelago, dated
    "yesterday" relative to call time so downstream pipeline tests have a
    plausible timestamp.
    """
    end = datetime.now(tz=UTC) - timedelta(hours=12)
    start = end - timedelta(minutes=10)
    return [
        SceneMetadata(
            scene_id="S1A_IW_GRDH_FIXTURE_LOFOTEN",
            name="S1A_IW_GRDH_1SDV_20260501T060000_20260501T060010_FIXTURE.zip",
            sensing_start=start,
            sensing_end=end,
            download_url="file:///fixtures/scene_metadata.json",
            bbox=(12.0, 67.5, 16.0, 69.0),
        )
    ]


async def search_recent_scenes(settings: Settings) -> list[SceneMetadata]:
    """Search the Copernicus catalog for IW GRD scenes covering the configured
    AOI in the last ``settings.lookback_hours`` hours.

    Falls back to :func:`_fixture_catalog` when credentials are blank.
    """
    if not settings.copernicus_username or not settings.copernicus_password:
        logger.warning("no Copernicus credentials; using fixture catalog")
        return _fixture_catalog()

    end = datetime.now(tz=UTC)
    start = end - timedelta(hours=settings.lookback_hours)
    west, south, east, north = (float(x) for x in settings.aoi_bbox.split(","))
    polygon = (
        f"POLYGON(({west} {south}, {east} {south}, {east} {north}, {west} {north}, {west} {south}))"
    )
    odata_filter = (
        "Collection/Name eq 'SENTINEL-1' "
        f"and ContentDate/Start ge {start.isoformat()}Z "
        f"and ContentDate/Start le {end.isoformat()}Z "
        f"and OData.CSC.Intersects(area=geography'SRID=4326;{polygon}') "
        "and contains(Name, 'GRDH')"
    )

    async with httpx.AsyncClient(timeout=30.0) as client:
        resp = await client.get(
            CATALOG_BASE,
            params={"$filter": odata_filter, "$top": "10", "$orderby": "ContentDate/Start desc"},
        )
        resp.raise_for_status()
        body = resp.json()

    scenes: list[SceneMetadata] = []
    for item in body.get("value", []):
        cd = item.get("ContentDate", {})
        scenes.append(
            SceneMetadata(
                scene_id=item["Id"],
                name=item["Name"],
                sensing_start=datetime.fromisoformat(cd["Start"].replace("Z", "+00:00")),
                sensing_end=datetime.fromisoformat(cd["End"].replace("Z", "+00:00")),
                download_url=f"{CATALOG_BASE}({item['Id']})/$value",
                bbox=(west, south, east, north),
            )
        )
    return scenes
