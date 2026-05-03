"""Postgres + S3 I/O for ship detection."""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from datetime import datetime
from typing import Any

import boto3
import psycopg
from botocore.client import Config

from .config import Settings

logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class SarDetectionRow:
    scene_id: str
    detected_at: datetime
    longitude: float
    latitude: float
    bbox_polygon_wkt: str | None
    confidence: float


def make_s3_client(settings: Settings) -> Any:
    return boto3.client(
        "s3",
        endpoint_url=settings.s3_endpoint,
        aws_access_key_id=settings.s3_access_key,
        aws_secret_access_key=settings.s3_secret_key,
        config=Config(signature_version="s3v4", connect_timeout=10, read_timeout=30),
        region_name="us-east-1",
    )


def fetch_tile_bytes(client: Any, bucket: str, key: str) -> bytes:
    obj = client.get_object(Bucket=bucket, Key=key)
    return bytes(obj["Body"].read())


def fetch_sidecar(client: Any, bucket: str, key: str) -> dict[str, Any]:
    obj = client.get_object(Bucket=bucket, Key=key)
    return json.loads(obj["Body"].read())


async def insert_detections(
    database_url: str,
    rows: list[SarDetectionRow],
) -> int:
    if not rows:
        return 0
    async with await psycopg.AsyncConnection.connect(database_url) as conn:
        async with conn.cursor() as cur:
            await cur.executemany(
                """
                INSERT INTO sar_detections (
                    scene_id, detected_at, geom, bbox_geom, confidence
                ) VALUES (
                    %s, %s,
                    ST_SetSRID(ST_MakePoint(%s, %s), 4326)::geography,
                    CASE WHEN %s IS NULL THEN NULL ELSE ST_GeogFromText(%s) END,
                    %s
                )
                """,
                [
                    (
                        r.scene_id,
                        r.detected_at,
                        r.longitude,
                        r.latitude,
                        r.bbox_polygon_wkt,
                        r.bbox_polygon_wkt,
                        r.confidence,
                    )
                    for r in rows
                ],
            )
        await conn.commit()
    return len(rows)
