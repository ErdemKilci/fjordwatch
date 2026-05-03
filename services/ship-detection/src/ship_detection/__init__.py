"""FjordWatch ship detection.

Runs YOLOv8 inference (ONNX) over Sentinel-1 SAR tiles delivered by the
sar-fetcher service, correlates each detection against AIS positions, and
writes the result to the ``sar_detections`` table.
"""

__version__ = "0.1.0"
