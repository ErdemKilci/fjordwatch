"""Periodic SAR fetch + tile + upload + notify ship-detection."""

from __future__ import annotations

import asyncio
import logging
import shutil
from pathlib import Path

import httpx
from apscheduler.schedulers.asyncio import AsyncIOScheduler

from .config import Settings
from .copernicus_client import SceneMetadata, search_recent_scenes
from .store import ensure_bucket, make_s3_client, scene_already_uploaded, upload_tile
from .tiler import TileMetadata, tile_scene

logger = logging.getLogger(__name__)


class FetchJob:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings

    async def run_once(self) -> int:
        """Fetch fresh scenes, tile each, push tiles, notify ship-detection.

        Returns the number of tiles uploaded across all scenes.
        """
        scenes = await search_recent_scenes(self._settings)
        if not scenes:
            logger.info("no fresh scenes in lookback window")
            return 0
        client = make_s3_client(self._settings)
        ensure_bucket(client, self._settings.s3_bucket)

        total_tiles = 0
        for scene in scenes:
            if scene_already_uploaded(client, self._settings.s3_bucket, scene.scene_id):
                logger.info("scene %s already in bucket; skipping", scene.scene_id)
                continue
            try:
                tile_meta = await self._tile_one(scene)
                for tile in tile_meta:
                    await upload_tile(client, self._settings.s3_bucket, scene.scene_id, tile.tile_path)
                await _notify_detection(self._settings, scene, tile_meta)
                total_tiles += len(tile_meta)
            except Exception:
                logger.exception("scene %s failed", scene.scene_id)
        return total_tiles

    async def _tile_one(self, scene: SceneMetadata) -> list[TileMetadata]:
        scene_dir = self._settings.work_dir / scene.scene_id
        if scene_dir.exists():
            shutil.rmtree(scene_dir)
        scene_dir.mkdir(parents=True)
        # Real download + decompress lives behind a Copernicus token; the
        # placeholder path uses the fixture URL directly via rasterio's VFS.
        return await asyncio.to_thread(
            tile_scene,
            Path(scene.download_url.replace("file://", "")),
            scene_dir,
            tile_size=self._settings.tile_size_px,
        )


async def _notify_detection(
    settings: Settings, scene: SceneMetadata, tiles: list[TileMetadata]
) -> None:
    """Best-effort: tell ship-detection a new scene's tiles are ready."""
    if not tiles:
        return
    payload = {
        "scene_id": scene.scene_id,
        "sensing_start": scene.sensing_start.isoformat(),
        "sensing_end": scene.sensing_end.isoformat(),
        "tiles": [
            {
                "tile_uri": f"s3://{settings.s3_bucket}/{scene.scene_id}/{t.tile_path.name}",
                "bounds_wgs84": list(t.bounds_wgs84),
            }
            for t in tiles
        ],
    }
    try:
        async with httpx.AsyncClient(timeout=10.0) as client:
            await client.post(settings.ship_detection_url, json=payload)
    except httpx.HTTPError:
        logger.exception("ship-detection notify failed; will retry on next tick")


def schedule(scheduler: AsyncIOScheduler, settings: Settings, job: FetchJob) -> None:
    scheduler.add_job(
        _job_wrapper,
        "interval",
        minutes=settings.fetch_interval_minutes,
        next_run_time=None,
        kwargs={"job": job},
        id="sar-fetch-tick",
        max_instances=1,
        coalesce=True,
    )


async def _job_wrapper(job: FetchJob) -> None:
    try:
        await job.run_once()
    except Exception:
        logger.exception("sar fetch tick failed")
