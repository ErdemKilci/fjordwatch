"""Service configuration loaded from environment variables."""

from __future__ import annotations

from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Singleton settings; values come from environment variables.

    All names are upper-snake-case to match the rest of the FjordWatch stack.
    """

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    database_url: str = Field(alias="DATABASE_URL")
    redis_url: str = Field(default="redis://redis:6379/0", alias="REDIS_URL")
    ais_stream: str = Field(default="ais:positions", alias="AIS_STREAM")

    model_dir: Path = Field(default=Path("/app/models"), alias="MODEL_DIR")
    mlflow_tracking_uri: str | None = Field(default=None, alias="MLFLOW_TRACKING_URI")
    mlflow_experiment: str = Field(default="fjordwatch-anomaly", alias="MLFLOW_EXPERIMENT")

    # Scoring window for each vessel: how far back from "now" we read positions.
    window_minutes: int = Field(default=360, alias="ANOMALY_WINDOW_MINUTES")
    # Cadence at which the scheduler scores every active vessel.
    scorer_interval_seconds: int = Field(default=600, alias="SCORER_INTERVAL_SECONDS")
    # Minimum positions in the window before we score a vessel.
    min_positions_per_window: int = Field(default=10, alias="MIN_POSITIONS_PER_WINDOW")
    # Score threshold below which results are not written (saves rows for quiet vessels).
    score_floor: float = Field(default=0.05, alias="SCORE_FLOOR")

    metrics_port: int = Field(default=8002, alias="ANOMALY_METRICS_PORT")
    log_level: str = Field(default="INFO", alias="LOG_LEVEL")


_settings: Settings | None = None


def get_settings() -> Settings:
    global _settings
    if _settings is None:
        _settings = Settings()  # type: ignore[call-arg]
    return _settings
