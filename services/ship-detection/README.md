# ship-detection (Python 3.12, FastAPI)

YOLOv8 ONNX inference over Sentinel-1 SAR tiles plus AIS correlation.
Receives a `POST /detect` payload with a list of tile URIs from
`sar-fetcher`, runs each through the ONNX model, and writes the result
to `sar_detections` correlated against the AIS `positions` stream.

## Pipeline

```
sar-fetcher POST /detect with { scene_id, tiles: [{ tile_uri, bounds_wgs84 }] }
    -> S3 GET tile + sidecar JSON
        -> SAR preprocess (sigma0 -> 0..1 -> 1x3xHxW float32)
            -> YOLOv8 ONNX inference -> bbox list
                -> bbox centroid -> WGS84 lon/lat via tile sidecar bounds
                    -> Postgres insert into sar_detections
                        -> correlator queries AIS, sets matched_mmsi/is_dark
```

## Endpoints

| Method | Path | Returns |
|---|---|---|
| GET | `/healthz` | `200 ok`. |
| GET | `/readyz` | `200 ready`. |
| POST | `/detect` | `{ detections, matched, dark }` after inference + correlation. |
| GET | `/metrics` | Prometheus exposition. |

## Run locally

```bash
make up
docker compose logs -f ship-detection
```

The service ships with a placeholder ONNX path that returns zero
detections until the developer drops a trained model into
`/app/models/yolov8_ship.onnx`. See `scripts/train.py` (added in phase 4
polish) for the fine-tuning workflow.

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `DATABASE_URL` | required | Postgres DSN (URL form). |
| `S3_ENDPOINT` | `http://minio:9000` | MinIO endpoint. |
| `S3_BUCKET_SAR` | `sar-tiles` | Tile bucket. |
| `MODEL_DIR` | `/app/models` | Where the ONNX model lives. |
| `SHIP_MODEL_FILENAME` | `yolov8_ship.onnx` | File to load. |
| `SHIP_CONFIDENCE_THRESHOLD` | `0.25` | YOLOv8 confidence cutoff. |
| `CORRELATION_RADIUS_M` | `500.0` | AIS match radius. |
| `CORRELATION_WINDOW_S` | `1800` | AIS match temporal window. |
| `SHIP_DETECTION_PORT` | `8001` | HTTP listen port. |
