"""SAR fetcher entrypoint: FastAPI + scheduler in one process."""

from __future__ import annotations

import asyncio
import logging
import signal
from contextlib import asynccontextmanager
from typing import Annotated, AsyncIterator

import structlog
import uvicorn
from apscheduler.schedulers.asyncio import AsyncIOScheduler
from fastapi import Depends, FastAPI
from fastapi.responses import JSONResponse
from prometheus_fastapi_instrumentator import Instrumentator

from .config import Settings, get_settings
from .scheduler import FetchJob, schedule

logger = logging.getLogger(__name__)


def _configure_logging(level: str) -> None:
    logging.basicConfig(format="%(message)s", level=level.upper())
    structlog.configure(
        processors=[
            structlog.processors.TimeStamper(fmt="iso"),
            structlog.processors.add_log_level,
            structlog.processors.JSONRenderer(),
        ]
    )


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    settings = get_settings()
    job = FetchJob(settings)
    scheduler = AsyncIOScheduler()
    schedule(scheduler, settings, job)
    scheduler.start()
    app.state.job = job
    app.state.scheduler = scheduler
    logger.info("sar-fetcher ready")
    try:
        yield
    finally:
        scheduler.shutdown()


app = FastAPI(title="FjordWatch SAR fetcher", version="0.1.0", lifespan=lifespan)
Instrumentator().instrument(app).expose(app, include_in_schema=False, endpoint="/metrics")


def get_settings_dep() -> Settings:
    return get_settings()


@app.get("/healthz")
async def healthz() -> JSONResponse:
    return JSONResponse({"status": "ok"})


@app.get("/readyz")
async def readyz() -> JSONResponse:
    return JSONResponse({"status": "ready"})


@app.post("/fetch-now")
async def fetch_now(_settings: Annotated[Settings, Depends(get_settings_dep)]) -> JSONResponse:
    job: FetchJob = app.state.job
    n = await job.run_once()
    return JSONResponse({"tiles_uploaded": n})


def main() -> None:
    settings = get_settings()
    _configure_logging(settings.log_level)
    config = uvicorn.Config(
        app,
        host="0.0.0.0",  # noqa: S104
        port=settings.metrics_port,
        log_level=settings.log_level.lower(),
        access_log=False,
    )
    server = uvicorn.Server(config)

    async def _serve() -> None:
        loop = asyncio.get_running_loop()
        stop = asyncio.Event()
        for sig in (signal.SIGINT, signal.SIGTERM):
            loop.add_signal_handler(sig, stop.set)
        server_task = asyncio.create_task(server.serve(), name="uvicorn")
        await stop.wait()
        server.should_exit = True
        await server_task

    asyncio.run(_serve())


if __name__ == "__main__":
    main()
