"""MinIO/S3 writer for SAR tiles."""

from __future__ import annotations

import asyncio
import logging
from pathlib import Path
from typing import Any

import boto3
from botocore.client import Config

from .config import Settings

logger = logging.getLogger(__name__)


def make_s3_client(settings: Settings) -> Any:
    return boto3.client(
        "s3",
        endpoint_url=settings.s3_endpoint,
        aws_access_key_id=settings.s3_access_key,
        aws_secret_access_key=settings.s3_secret_key,
        config=Config(signature_version="s3v4", connect_timeout=10, read_timeout=30),
        region_name="us-east-1",
    )


def ensure_bucket(client: Any, bucket: str) -> None:
    existing = {b["Name"] for b in client.list_buckets().get("Buckets", [])}
    if bucket not in existing:
        client.create_bucket(Bucket=bucket)


def scene_already_uploaded(client: Any, bucket: str, scene_id: str) -> bool:
    resp = client.list_objects_v2(Bucket=bucket, Prefix=f"{scene_id}/", MaxKeys=1)
    return resp.get("KeyCount", 0) > 0


async def upload_tile(client: Any, bucket: str, scene_id: str, path: Path) -> str:
    """Upload a single tile + sidecar pair to MinIO under ``scene_id/``.

    Returns the s3:// URI of the tile (not the sidecar).
    """
    key = f"{scene_id}/{path.name}"
    sidecar_key = f"{scene_id}/{path.with_suffix('.json').name}"
    sidecar_path = path.with_suffix(".json")
    await asyncio.to_thread(
        client.upload_file, str(path), bucket, key, ExtraArgs={"ContentType": "image/png"}
    )
    if sidecar_path.exists():
        await asyncio.to_thread(
            client.upload_file,
            str(sidecar_path),
            bucket,
            sidecar_key,
            ExtraArgs={"ContentType": "application/json"},
        )
    return f"s3://{bucket}/{key}"
