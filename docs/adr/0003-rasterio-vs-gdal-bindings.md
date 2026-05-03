# 0003. rasterio over the GDAL Python bindings

- **Status:** Accepted
- **Date:** 2026-05-03
- **Deciders:** Erdem Kilci

## Context

Phase 4 (dark vessel detection) needs to open Sentinel-1 GRD scenes,
extract per-pixel intensity, and write fixed-size tiles plus geotransform
metadata. Two reasonable Python paths exist: the official GDAL Python
bindings (`osgeo.gdal`, `osgeo.osr`) and `rasterio`, a wrapper around the
same GDAL C++ libraries with a Pythonic API.

## Decision

Use **rasterio 1.4** in `services/sar-fetcher`. Pin the system runtime
deps (`libgdal32`, `libproj25`, `libgeos-c1v5`, `libtiff6`) at Dockerfile
build time. Do not install GDAL Python bindings.

## Considered alternatives

- **GDAL Python bindings.** Pros: official, exposes every GDAL knob. Cons: API is a thin wrapper over C++ idioms (out-parameters, manual error code checks, opaque dataset/band lifetimes); installation pulls dev headers; Python typing is weak.
- **rioxarray.** Pros: nice xarray integration. Cons: extra dependency we do not need for tile + sidecar JSON output.
- **A homegrown reader for TIFF/GeoTIFF.** Pros: smallest dependency footprint. Cons: Sentinel-1 GRD products use GeoTIFF features (cloud-optimized blocks, internal masks, COG tiling) that are non-trivial to implement correctly.

## Consequences

- **Positive:** rasterio's `open` + `read` + `windows` API maps cleanly to our tiling loop. Errors raise Python exceptions with context. The Docker image stays small because we only install the runtime libgdal, not the dev headers.
- **Negative:** rasterio's release cadence trails core GDAL by a few months; advanced raster operations may require dropping to `osgeo.gdal` later. We pin a recent rasterio (1.4) to minimize the gap.
- **Follow-ups:** if phase 4 polish adds Doppler corrections or real Sentinel-1 calibration, revisit whether `osgeo.gdal` or a thin C extension is needed.

## References

- rasterio docs: https://rasterio.readthedocs.io/
- GDAL Python bindings: https://gdal.org/api/python_bindings.html
