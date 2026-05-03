"""APScheduler job that scores every active vessel on a fixed cadence."""

from __future__ import annotations

import asyncio
import logging

from apscheduler.schedulers.asyncio import AsyncIOScheduler

from .config import Settings
from .ensemble import EnsembleScorer
from .features import compute_features
from .lstm_ae import resample_trajectory
from .store import (
    open_pool,
    stream_active_vessel_windows,
    utc_now,
    window_since,
    write_anomalies,
)

logger = logging.getLogger(__name__)


class ScoringJob:
    def __init__(self, settings: Settings, scorer: EnsembleScorer) -> None:
        self._settings = settings
        self._scorer = scorer

    async def run_once(self) -> int:
        """Score every active vessel once. Returns the number of rows written."""
        now = utc_now()
        since = window_since(now, minutes=self._settings.window_minutes)
        conn = await open_pool(self._settings.database_url)
        rows_written = 0
        try:
            feature_rows = []
            sequences = []
            async for _mmsi, df in stream_active_vessel_windows(conn, since=since):
                if len(df) < self._settings.min_positions_per_window:
                    continue
                features = compute_features(df, now_utc=now)
                if features is None:
                    continue
                feature_rows.append(features)
                sequences.append(resample_trajectory(df))

            if not feature_rows:
                logger.debug("scoring tick: no eligible vessels")
                return 0

            import numpy as np

            sequence_array = np.stack(sequences)
            results = self._scorer.score(feature_rows, sequence_array)
            rows_written = await write_anomalies(
                conn,
                results=results,
                window_start=since,
                window_end=now,
                score_floor=self._settings.score_floor,
            )
            logger.info(
                "scoring tick complete",
                extra={"vessels_scored": len(results), "rows_written": rows_written},
            )
        finally:
            await conn.close()
        return rows_written


def schedule(scheduler: AsyncIOScheduler, job: ScoringJob, settings: Settings) -> None:
    scheduler.add_job(
        _run_job_wrapper,
        "interval",
        seconds=settings.scorer_interval_seconds,
        next_run_time=None,
        kwargs={"job": job},
        id="score-active-vessels",
        max_instances=1,
        coalesce=True,
    )


async def _run_job_wrapper(job: ScoringJob) -> None:
    try:
        await job.run_once()
    except Exception:
        logger.exception("scoring tick failed")


def run_forever(settings: Settings, scorer: EnsembleScorer) -> None:
    scheduler = AsyncIOScheduler()
    job = ScoringJob(settings, scorer)
    schedule(scheduler, job, settings)
    scheduler.start()
    try:
        asyncio.get_event_loop().run_forever()
    finally:
        scheduler.shutdown()
