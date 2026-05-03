# syntax=docker/dockerfile:1.7
FROM python:3.12-slim-bookworm AS runtime

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

# rasterio + GDAL system deps. We pin the runtime libs but not the dev
# headers to keep the image lean.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
        curl libgdal32 libproj25 libgeos-c1v5 libtiff6 \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd --system --gid 10001 app \
 && useradd --system --uid 10001 --gid app --no-create-home --shell /usr/sbin/nologin app

WORKDIR /app

COPY pyproject.toml ./
COPY src ./src
COPY scripts ./scripts

RUN pip install .

USER app:app
EXPOSE 8003
ENV SAR_FETCHER_PORT=8003 \
    SAR_WORK_DIR=/tmp/sar

HEALTHCHECK --interval=20s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --silent --fail http://127.0.0.1:8003/healthz || exit 1

ENTRYPOINT ["python", "-m", "sar_fetcher.main"]
