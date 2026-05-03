"""FastAPI surface for the anomaly detection service."""

from __future__ import annotations

import logging
from contextlib import asynccontextmanager
from datetime import UTC, datetime
from typing import Annotated

import numpy as np
from fastapi import Depends, FastAPI, HTTPException
from fastapi.responses import JSONResponse
from prometheus_fastapi_instrumentator import Instrumentator
from pydantic import BaseModel, Field

from .config import Settings, get_settings
from .ensemble import EnsembleScorer
from .features import FEATURE_NAMES, compute_features
from .isoforest import IsoForestScorer
from .lstm_ae import resample_trajectory
from .store import open_pool, read_window, window_since

logger = logging.getLogger(__name__)


class ScoreRequest(BaseModel):
    mmsi: int = Field(gt=0)
    window_minutes: int | None = Field(default=None, gt=0, le=1440)


class ContributingFeature(BaseModel):
    name: str
    value: float


class ScoreResponse(BaseModel):
    mmsi: int
    score: float
    iso_score: float
    lstm_score: float
    window_start: datetime
    window_end: datetime
    point_count: int
    contributing: list[ContributingFeature]
    model_versions: dict[str, str]


def _load_scorer(settings: Settings) -> EnsembleScorer:
    iso_path = settings.model_dir / "isoforest.pkl"
    if iso_path.exists():
        iso = IsoForestScorer.load(iso_path)
    else:
        logger.warning(
            "no trained isoforest found; using a synthetic-fit fallback. Retrain with anomaly-train."
        )
        iso = _bootstrap_isoforest()
    return EnsembleScorer(iso=iso, lstm=None)


def _bootstrap_isoforest() -> IsoForestScorer:
    """Fit an IsoForest on a tiny synthetic dataset so /score answers
    deterministically before the real training run lands."""
    rng = np.random.default_rng(42)
    rows = rng.normal(loc=0.0, scale=1.0, size=(256, len(FEATURE_NAMES))).astype(np.float32)
    import pandas as pd

    df = pd.DataFrame(rows, columns=list(FEATURE_NAMES))
    iso = IsoForestScorer()
    iso.fit(df)
    return iso


@asynccontextmanager
async def lifespan(app: FastAPI):  # type: ignore[no-untyped-def]
    settings = get_settings()
    app.state.settings = settings
    app.state.scorer = _load_scorer(settings)
    logger.info("anomaly-detection ready", extra={"model_dir": str(settings.model_dir)})
    yield


app = FastAPI(title="FjordWatch anomaly detection", version="0.1.0", lifespan=lifespan)
Instrumentator().instrument(app).expose(app, include_in_schema=False, endpoint="/metrics")


def get_settings_dep() -> Settings:
    return get_settings()


@app.get("/healthz")
async def healthz() -> JSONResponse:
    return JSONResponse({"status": "ok"})


@app.get("/readyz")
async def readyz(settings: Annotated[Settings, Depends(get_settings_dep)]) -> JSONResponse:
    try:
        conn = await open_pool(settings.database_url)
        await conn.close()
        return JSONResponse({"status": "ready"})
    except Exception as exc:
        return JSONResponse({"status": "not_ready", "error": str(exc)}, status_code=503)


@app.post("/score", response_model=ScoreResponse)
async def score(
    req: ScoreRequest, settings: Annotated[Settings, Depends(get_settings_dep)]
) -> ScoreResponse:
    minutes = req.window_minutes or settings.window_minutes
    now = datetime.now(tz=UTC)
    since = window_since(now, minutes=minutes)
    conn = await open_pool(settings.database_url)
    try:
        df = await read_window(conn, mmsi=req.mmsi, since=since)
    finally:
        await conn.close()
    if df.empty or len(df) < 2:
        raise HTTPException(status_code=404, detail="not enough positions in window")

    features = compute_features(df, now_utc=now)
    if features is None:
        raise HTTPException(status_code=404, detail="not enough positions in window")

    scorer: EnsembleScorer = app.state.scorer
    sequence = resample_trajectory(df)
    sequences = sequence[None, ...]
    results = scorer.score([features], sequences)
    if not results:
        raise HTTPException(status_code=500, detail="scorer returned no results")
    result = results[0]
    return ScoreResponse(
        mmsi=result.mmsi,
        score=result.score,
        iso_score=result.iso_score,
        lstm_score=result.lstm_score,
        window_start=features.window_start.to_pydatetime(),
        window_end=features.window_end.to_pydatetime(),
        point_count=features.point_count,
        contributing=[ContributingFeature(name=k, value=v) for k, v in result.contributing.items()],
        model_versions=result.model_versions,
    )
