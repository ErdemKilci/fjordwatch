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
COPY seed ./seed

# Default: stub mode. Set EMBEDDING_STUB=0 and pip install '.[heavy]' for the
# real model. Keeping the default lean preserves the 'works out of the box'
# story for first-time runs.
RUN pip install .

USER app:app
EXPOSE 8004
ENV EMBEDDING_PORT=8004 \
    EMBEDDING_STUB=1

HEALTHCHECK --interval=20s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --silent --fail http://127.0.0.1:8004/healthz || exit 1

ENTRYPOINT ["python", "-m", "embedding.main"]
