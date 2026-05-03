"""Service entrypoint: FastAPI + the scheduler in one process."""

from __future__ import annotations

import asyncio
import logging
import signal

import structlog
import uvicorn
from apscheduler.schedulers.asyncio import AsyncIOScheduler

from .api import app as fastapi_app
from .config import Settings, get_settings
from .ensemble import EnsembleScorer
from .scheduler import ScoringJob, schedule


def _configure_logging(level: str) -> None:
    logging.basicConfig(format="%(message)s", level=level.upper())
    structlog.configure(
        processors=[
            structlog.processors.TimeStamper(fmt="iso"),
            structlog.processors.add_log_level,
            structlog.processors.JSONRenderer(),
        ]
    )


async def _amain(settings: Settings, scorer: EnsembleScorer) -> None:
    scheduler = AsyncIOScheduler()
    job = ScoringJob(settings, scorer)
    schedule(scheduler, job, settings)
    scheduler.start()

    config = uvicorn.Config(
        fastapi_app,
        host="0.0.0.0",  # noqa: S104 (container-bound)
        port=settings.metrics_port,
        log_level=settings.log_level.lower(),
        access_log=False,
    )
    server = uvicorn.Server(config)

    stop_event = asyncio.Event()
    loop = asyncio.get_running_loop()
    for sig in (signal.SIGINT, signal.SIGTERM):
        loop.add_signal_handler(sig, stop_event.set)

    server_task = asyncio.create_task(server.serve(), name="uvicorn")
    stop_task = asyncio.create_task(stop_event.wait(), name="stop")
    done, _ = await asyncio.wait({server_task, stop_task}, return_when=asyncio.FIRST_COMPLETED)

    scheduler.shutdown()
    server.should_exit = True
    for task in done:
        if task is server_task:
            await task


def main() -> None:
    settings = get_settings()
    _configure_logging(settings.log_level)
    from .api import _load_scorer

    scorer = _load_scorer(settings)
    asyncio.run(_amain(settings, scorer))


if __name__ == "__main__":
    main()
