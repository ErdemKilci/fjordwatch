# syntax=docker/dockerfile:1.7
FROM python:3.12-slim-bookworm AS runtime

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl libpq5 \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd --system --gid 10001 app \
 && useradd --system --uid 10001 --gid app --no-create-home --shell /usr/sbin/nologin app

WORKDIR /app

COPY pyproject.toml ./
COPY src ./src
COPY scripts ./scripts

# CPU-only torch wheel to keep the image small. The pyproject pin is upper-bounded
# at 3.0 so a CUDA wheel is never selected on a non-GPU host.
RUN pip install --extra-index-url https://download.pytorch.org/whl/cpu .

RUN mkdir -p /app/models && chown -R app:app /app

USER app:app
EXPOSE 8002
ENV ANOMALY_METRICS_PORT=8002 \
    MODEL_DIR=/app/models

HEALTHCHECK --interval=20s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --silent --fail http://127.0.0.1:8002/healthz || exit 1

ENTRYPOINT ["python", "-m", "anomaly_detection.main"]
