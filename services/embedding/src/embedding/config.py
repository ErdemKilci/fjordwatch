from __future__ import annotations

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    model_name: str = Field(default="intfloat/multilingual-e5-large", alias="EMBEDDING_MODEL")
    dimension: int = Field(default=1024, alias="EMBEDDING_DIMENSION")
    stub: bool = Field(default=False, alias="EMBEDDING_STUB")
    metrics_port: int = Field(default=8004, alias="EMBEDDING_PORT")
    log_level: str = Field(default="INFO", alias="LOG_LEVEL")


_settings: Settings | None = None


def get_settings() -> Settings:
    global _settings
    if _settings is None:
        _settings = Settings()
    return _settings
