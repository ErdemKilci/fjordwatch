"""FastAPI surface for the embedding service."""

from __future__ import annotations

import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Annotated

from fastapi import Depends, FastAPI, HTTPException
from fastapi.responses import JSONResponse
from prometheus_fastapi_instrumentator import Instrumentator
from pydantic import BaseModel, Field

from .config import Settings, get_settings
from .model import EmbeddingModel, load_real_model, load_stub_model

logger = logging.getLogger(__name__)


class EmbedRequest(BaseModel):
    text: str = Field(min_length=1, max_length=8000)


class EmbedResponse(BaseModel):
    embedding: list[float]
    model: str
    dimension: int


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncIterator[None]:
    settings = get_settings()
    if settings.stub:
        model = load_stub_model(settings.dimension)
    else:
        model = load_real_model(settings.model_name, settings.dimension)
    app.state.model = model
    app.state.settings = settings
    logger.info("embedding service ready stub=%s dim=%d", settings.stub, settings.dimension)
    yield


app = FastAPI(title="FjordWatch embedding", version="0.1.0", lifespan=lifespan)
Instrumentator().instrument(app).expose(app, include_in_schema=False, endpoint="/metrics")


def get_settings_dep() -> Settings:
    return get_settings()


@app.get("/healthz")
async def healthz() -> JSONResponse:
    return JSONResponse({"status": "ok"})


@app.get("/readyz")
async def readyz() -> JSONResponse:
    return JSONResponse({"status": "ready"})


@app.post("/embed", response_model=EmbedResponse)
async def embed(
    req: EmbedRequest, settings: Annotated[Settings, Depends(get_settings_dep)]
) -> EmbedResponse:
    model: EmbeddingModel = app.state.model
    try:
        vec = model.embed(req.text)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    return EmbedResponse(
        embedding=[float(x) for x in vec],
        model=settings.model_name if not settings.stub else "stub",
        dimension=int(model.dimension),
    )
