# sar-fetcher (Python 3.12, FastAPI)

Pulls fresh Sentinel-1 GRD scenes covering the Norwegian coast on a schedule, tiles them with rasterio, pushes the tiles to MinIO, and notifies the ship-detection service.

## Pipeline

```
Copernicus Data Space (or fixture catalog when COPERNICUS_USERNAME unset)
    -> rasterio open -> sigma0 dB calibration + percentile stretch
        -> 1024x1024 PNG tiles + sidecar JSON (bounds, geotransform)
            -> MinIO bucket (sar-tiles)
                -> POST /detect on the ship-detection service
```

## Run locally

```bash
make up
docker compose logs -f sar-fetcher
docker compose exec sar-fetcher curl -X POST http://localhost:8003/fetch-now
```

## Endpoints

| Method | Path | Returns |
|---|---|---|
| GET | `/healthz` | `200 ok` once boot completes. |
| GET | `/readyz` | `200 ready`. |
| POST | `/fetch-now` | Run a fetch tick immediately and return the number of tiles uploaded. |
| GET | `/metrics` | Prometheus exposition. |

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `S3_ENDPOINT` | `http://minio:9000` | MinIO endpoint. |
| `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` | dev defaults | Bucket credentials. |
| `S3_BUCKET_SAR` | `sar-tiles` | Bucket name for tiles. |
| `COPERNICUS_USERNAME` / `COPERNICUS_PASSWORD` | unset | Real catalog access. When blank, the fixture catalog is used. |
| `SAR_AOI_BBOX` | `4,58,32,72` | West/south/east/north covering Norway. |
| `SAR_FETCH_INTERVAL_MINUTES` | `720` | How often to look for new scenes. |
| `SAR_LOOKBACK_HOURS` | `24` | How far back the search looks. |
| `SAR_TILE_SIZE_PX` | `1024` | Tile dimension. |
| `SHIP_DETECTION_URL` | `http://ship-detection:8001/detect` | Notification target. |
| `SAR_FETCHER_PORT` | `8003` | HTTP listen port. |
