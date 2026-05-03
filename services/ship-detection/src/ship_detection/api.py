"""FastAPI surface for ship detection."""

from __future__ import annotations

import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from datetime import UTC, datetime
from typing import Annotated

from fastapi import Depends, FastAPI
from fastapi.responses import JSONResponse
from prometheus_fastapi_instrumentator import Instrumentator
from pydantic import BaseModel, Field

from .config import Settings, get_settings
from .correlator import correlate_recent
from .inference import ShipDetector
from .sar_preprocess import load_tile
from .store import (
    SarDetectionRow,
    fetch_sidecar,
    fetch_tile_bytes,
    insert_detections,
    make_s3_client,
)

logger = logging.getLogger(__name__)


class TileRequest(BaseModel):
    tile_uri: str = Field(description="s3:// URI of the tile PNG")
    bounds_wgs84: list[float] = Field(description="[west, south, east, north]")


class DetectRequest(BaseModel):
    scene_id: str
    sensing_start: datetime
    sensing_end: datetime
    tiles: list[TileRequest]


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    settings = get_settings()
    detector = ShipDetector(
        model_path=settings.model_dir / settings.model_filename,
        confidence_threshold=settings.confidence_threshold,
    )
    detector.warm_up()
    app.state.settings = settings
    app.state.detector = detector
    app.state.s3 = make_s3_client(settings)
    logger.info("ship-detection ready")
    yield


app = FastAPI(title="FjordWatch ship detection", version="0.1.0", lifespan=lifespan)
Instrumentator().instrument(app).expose(app, include_in_schema=False, endpoint="/metrics")


def get_settings_dep() -> Settings:
    return get_settings()


@app.get("/healthz")
async def healthz() -> JSONResponse:
    return JSONResponse({"status": "ok"})


@app.get("/readyz")
async def readyz() -> JSONResponse:
    return JSONResponse({"status": "ready"})


@app.post("/detect")
async def detect(
    req: DetectRequest, settings: Annotated[Settings, Depends(get_settings_dep)]
) -> JSONResponse:
    """Run ship detection on every tile in the request, persist results, and
    correlate against AIS positions."""
    detector: ShipDetector = app.state.detector
    s3 = app.state.s3

    rows: list[SarDetectionRow] = []
    for tile in req.tiles:
        bucket, key = _split_s3_uri(tile.tile_uri)
        try:
            png_bytes = fetch_tile_bytes(s3, bucket, key)
            sidecar_key = key.rsplit(".", 1)[0] + ".json"
            sidecar = fetch_sidecar(s3, bucket, sidecar_key)
        except Exception:
            logger.exception("tile fetch failed: %s", tile.tile_uri)
            continue

        arr = load_tile(png_bytes)
        detections = detector.detect(arr)
        if not detections:
            continue

        for det in detections:
            lon, lat = _bbox_centroid_to_wgs84(det.bbox_pixels, sidecar)
            rows.append(
                SarDetectionRow(
                    scene_id=req.scene_id,
                    detected_at=req.sensing_start,
                    longitude=lon,
                    latitude=lat,
                    bbox_polygon_wkt=None,
                    confidence=det.confidence,
                )
            )

    inserted = await insert_detections(settings.database_url, rows)

    correlation_window_start = (
        req.sensing_start.replace(tzinfo=UTC)
        if req.sensing_start.tzinfo is None
        else req.sensing_start
    )
    report = await correlate_recent(settings, correlation_window_start)

    return JSONResponse(
        {
            "detections": inserted,
            "matched": report["matched"],
            "dark": report["dark"],
        }
    )


def _split_s3_uri(uri: str) -> tuple[str, str]:
    if not uri.startswith("s3://"):
        raise ValueError(f"expected s3:// URI, got {uri}")
    rest = uri[len("s3://") :]
    bucket, _, key = rest.partition("/")
    return bucket, key


def _bbox_centroid_to_wgs84(
    bbox_pixels: tuple[float, float, float, float],
    sidecar: dict[str, object],
) -> tuple[float, float]:
    """Map the centroid of a pixel bbox back to lon/lat using the tile's
    WGS84 bounds.

    This is an approximation that assumes the tile bounds enclose a roughly
    rectangular region in lon/lat space; for a full GRD that's true within a
    rounding error. The sidecar's ``bounds_wgs84`` is ``[west, south, east, north]``.
    """
    bounds = sidecar["bounds_wgs84"]
    if not isinstance(bounds, list) or len(bounds) != 4:
        return (0.0, 0.0)
    west, south, east, north = (float(b) for b in bounds)
    tile_size = sidecar.get("tile_size", [1024, 1024])
    if not isinstance(tile_size, list) or len(tile_size) != 2:
        tile_size = [1024, 1024]
    tw = float(tile_size[0])
    th = float(tile_size[1])
    cx = (bbox_pixels[0] + bbox_pixels[2]) / 2.0
    cy = (bbox_pixels[1] + bbox_pixels[3]) / 2.0
    lon = west + (cx / tw) * (east - west)
    # Pixel y grows downward; latitude grows northward.
    lat = north - (cy / th) * (north - south)
    return (lon, lat)
