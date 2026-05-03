from __future__ import annotations

from ship_detection.api import _bbox_centroid_to_wgs84, _split_s3_uri


def test_split_s3_uri_round_trip() -> None:
    bucket, key = _split_s3_uri("s3://sar-tiles/scene-id/r000000_c000000.png")
    assert bucket == "sar-tiles"
    assert key == "scene-id/r000000_c000000.png"


def test_bbox_centroid_lon_lat_lies_inside_bounds() -> None:
    sidecar = {
        "bounds_wgs84": [12.0, 67.5, 16.0, 69.0],
        "tile_size": [1024, 1024],
    }
    lon, lat = _bbox_centroid_to_wgs84((0.0, 0.0, 1024.0, 1024.0), sidecar)
    assert 12.0 <= lon <= 16.0
    assert 67.5 <= lat <= 69.0


def test_bbox_centroid_lat_grows_north_at_top() -> None:
    sidecar = {
        "bounds_wgs84": [12.0, 67.5, 16.0, 69.0],
        "tile_size": [1024, 1024],
    }
    _, lat_top = _bbox_centroid_to_wgs84((0.0, 0.0, 100.0, 100.0), sidecar)
    _, lat_bot = _bbox_centroid_to_wgs84((0.0, 900.0, 100.0, 1000.0), sidecar)
    assert lat_top > lat_bot
