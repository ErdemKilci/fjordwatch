"""Service configuration."""

from __future__ import annotations

from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    database_url: str = Field(alias="DATABASE_URL")
    s3_endpoint: str = Field(default="http://minio:9000", alias="S3_ENDPOINT")
    s3_access_key: str = Field(default="fjordwatch", alias="MINIO_ROOT_USER")
    s3_secret_key: str = Field(default="fjordwatch_dev_only_change_me", alias="MINIO_ROOT_PASSWORD")
    s3_bucket: str = Field(default="sar-tiles", alias="S3_BUCKET_SAR")

    model_dir: Path = Field(default=Path("/app/models"), alias="MODEL_DIR")
    model_filename: str = Field(default="yolov8_ship.onnx", alias="SHIP_MODEL_FILENAME")
    confidence_threshold: float = Field(default=0.25, alias="SHIP_CONFIDENCE_THRESHOLD")

    correlation_radius_m: float = Field(default=500.0, alias="CORRELATION_RADIUS_M")
    correlation_window_s: int = Field(default=1800, alias="CORRELATION_WINDOW_S")

    metrics_port: int = Field(default=8001, alias="SHIP_DETECTION_PORT")
    log_level: str = Field(default="INFO", alias="LOG_LEVEL")


_settings: Settings | None = None


def get_settings() -> Settings:
    global _settings
    if _settings is None:
        _settings = Settings()  # type: ignore[call-arg]
    return _settings
