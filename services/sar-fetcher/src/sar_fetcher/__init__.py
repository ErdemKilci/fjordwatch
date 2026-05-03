"""FjordWatch SAR fetcher.

Pulls fresh Sentinel-1 GRD scenes covering the Norwegian coast on a
schedule, tiles them with rasterio, and pushes the tiles to MinIO. The
ship-detection service consumes the resulting tiles via S3 URIs.
"""

__version__ = "0.1.0"
