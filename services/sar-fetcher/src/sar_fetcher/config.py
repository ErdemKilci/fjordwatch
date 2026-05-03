"""SAR fetcher configuration loaded from environment."""

from __future__ import annotations

from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    s3_endpoint: str = Field(default="http://minio:9000", alias="S3_ENDPOINT")
    s3_access_key: str = Field(default="fjordwatch", alias="MINIO_ROOT_USER")
    s3_secret_key: str = Field(default="fjordwatch_dev_only_change_me", alias="MINIO_ROOT_PASSWORD")
    s3_bucket: str = Field(default="sar-tiles", alias="S3_BUCKET_SAR")

    copernicus_username: str | None = Field(default=None, alias="COPERNICUS_USERNAME")
    copernicus_password: str | None = Field(default=None, alias="COPERNICUS_PASSWORD")

    aoi_bbox: str = Field(default="4,58,32,72", alias="SAR_AOI_BBOX")
    fetch_interval_minutes: int = Field(default=720, alias="SAR_FETCH_INTERVAL_MINUTES")
    lookback_hours: int = Field(default=24, alias="SAR_LOOKBACK_HOURS")

    tile_size_px: int = Field(default=1024, alias="SAR_TILE_SIZE_PX")
    work_dir: Path = Field(default=Path("/tmp/sar"), alias="SAR_WORK_DIR")

    ship_detection_url: str = Field(
        default="http://ship-detection:8001/detect",
        alias="SHIP_DETECTION_URL",
    )

    metrics_port: int = Field(default=8003, alias="SAR_FETCHER_PORT")
    log_level: str = Field(default="INFO", alias="LOG_LEVEL")


_settings: Settings | None = None


def get_settings() -> Settings:
    global _settings
    if _settings is None:
        _settings = Settings()
    return _settings
