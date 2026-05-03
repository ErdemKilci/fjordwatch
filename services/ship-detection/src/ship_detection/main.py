"""Entrypoint."""

from __future__ import annotations

import logging

import structlog
import uvicorn

from .api import app
from .config import get_settings


def _configure_logging(level: str) -> None:
    logging.basicConfig(format="%(message)s", level=level.upper())
    structlog.configure(
        processors=[
            structlog.processors.TimeStamper(fmt="iso"),
            structlog.processors.add_log_level,
            structlog.processors.JSONRenderer(),
        ]
    )


def main() -> None:
    settings = get_settings()
    _configure_logging(settings.log_level)
    uvicorn.run(
        app,
        host="0.0.0.0",
        port=settings.metrics_port,
        log_level=settings.log_level.lower(),
        access_log=False,
    )


if __name__ == "__main__":
    main()
