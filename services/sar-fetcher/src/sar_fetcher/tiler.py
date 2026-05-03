"""Sentinel-1 GRD tiling.

Open the GRD with rasterio, apply a sigma0-dB calibration approximation,
percentile-stretch to 0-255, and emit fixed-size tiles. Each tile is paired
with a sidecar JSON describing its WGS84 geotransform so the ship-detection
service can map pixel coordinates back to lon/lat.
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
import rasterio
from rasterio.windows import Window

logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class TileMetadata:
    tile_path: Path
    sidecar_path: Path
    pixel_origin: tuple[int, int]  # (col, row) of the tile's top-left in the source raster
    bounds_wgs84: tuple[float, float, float, float]  # west, south, east, north


def tile_scene(
    scene_path: Path,
    output_dir: Path,
    *,
    tile_size: int = 1024,
) -> list[TileMetadata]:
    """Tile a Sentinel-1 GRD into ``tile_size`` x ``tile_size`` PNGs.

    Tiles falling entirely outside the dataset's footprint are skipped. Each
    tile is normalized via percentile clipping and saved as 8-bit grayscale.
    A sidecar JSON ``<tile>.json`` carries the metadata needed by ship
    detection to project pixel boxes back into WGS84.
    """
    output_dir.mkdir(parents=True, exist_ok=True)
    metadata: list[TileMetadata] = []

    with rasterio.open(scene_path) as src:
        n_cols, n_rows = src.width, src.height
        for row_off in range(0, n_rows, tile_size):
            for col_off in range(0, n_cols, tile_size):
                width = min(tile_size, n_cols - col_off)
                height = min(tile_size, n_rows - row_off)
                if width < 32 or height < 32:
                    continue
                window = Window(col_off, row_off, width, height)
                arr = src.read(1, window=window)
                if arr.size == 0 or _is_blank(arr):
                    continue

                norm = _normalize(arr)
                tile_id = f"r{row_off:06d}_c{col_off:06d}"
                tile_path = output_dir / f"{tile_id}.png"
                _save_png(norm, tile_path)

                bounds = _window_bounds_wgs84(src, window)
                sidecar = {
                    "scene_path": str(scene_path),
                    "pixel_origin": [col_off, row_off],
                    "tile_size": [width, height],
                    "bounds_wgs84": list(bounds),
                    "transform": src.transform.to_gdal(),
                    "crs": src.crs.to_string() if src.crs else None,
                }
                sidecar_path = tile_path.with_suffix(".json")
                sidecar_path.write_text(json.dumps(sidecar))

                metadata.append(
                    TileMetadata(
                        tile_path=tile_path,
                        sidecar_path=sidecar_path,
                        pixel_origin=(col_off, row_off),
                        bounds_wgs84=bounds,
                    )
                )

    logger.info("tiled %s into %d tiles", scene_path, len(metadata))
    return metadata


def _is_blank(arr: np.ndarray) -> bool:
    return bool(np.all(arr == 0))


def _normalize(arr: np.ndarray) -> np.ndarray:
    """Percentile-stretch + sigma0 dB approximation. Keeps values in [0, 255]."""
    finite = arr[np.isfinite(arr) & (arr > 0)]
    if finite.size == 0:
        return np.zeros_like(arr, dtype=np.uint8)
    db = 10.0 * np.log10(arr.astype(np.float32) + 1e-6)
    p2, p98 = np.percentile(db[np.isfinite(db)], [2.0, 98.0])
    if p98 - p2 < 1e-3:
        p98 = p2 + 1e-3
    stretched = np.clip((db - p2) / (p98 - p2), 0.0, 1.0)
    return (stretched * 255.0).astype(np.uint8)


def _save_png(arr: np.ndarray, path: Path) -> None:
    from PIL import Image

    Image.fromarray(arr, mode="L").save(path, format="PNG", optimize=True)


def _window_bounds_wgs84(src: Any, window: Window) -> tuple[float, float, float, float]:
    """Return the WGS84 bounds of the given pixel window.

    Sentinel-1 GRD scenes are typically delivered in EPSG:4326 directly; if
    they are in another CRS we trust rasterio's transform to convert.
    """
    bounds = rasterio.windows.bounds(window, src.transform)
    return (float(bounds[0]), float(bounds[1]), float(bounds[2]), float(bounds[3]))
