"""AIS correlation for SAR detections.

For each fresh detection, find the nearest AIS broadcast within
``CORRELATION_RADIUS_M`` and ``CORRELATION_WINDOW_S``. Update the row in
``sar_detections`` with ``matched_mmsi``, ``match_distance_m``,
``match_lag_s``, and ``is_dark``.
"""

from __future__ import annotations

import logging
from datetime import datetime, timedelta

import psycopg

from .config import Settings

logger = logging.getLogger(__name__)


async def correlate_recent(settings: Settings, since: datetime) -> dict[str, int]:
    """Run correlation for every uncorrelated detection since ``since``.

    Returns a small report dict for logging/metrics.
    """
    matched = 0
    dark = 0
    async with await psycopg.AsyncConnection.connect(settings.database_url) as conn:
        async with conn.cursor() as cur:
            await cur.execute(
                """
                SELECT id, detected_at, ST_X(geom::geometry), ST_Y(geom::geometry)
                FROM sar_detections
                WHERE matched_mmsi IS NULL
                  AND match_distance_m IS NULL
                  AND detected_at >= %s
                """,
                (since,),
            )
            pending = await cur.fetchall()

        for det_id, detected_at, lon, lat in pending:
            window_start = detected_at - timedelta(seconds=settings.correlation_window_s)
            window_end = detected_at + timedelta(seconds=settings.correlation_window_s)
            async with conn.cursor() as cur:
                await cur.execute(
                    """
                    SELECT mmsi, ts,
                           ST_Distance(
                               geom,
                               ST_SetSRID(ST_MakePoint(%s, %s), 4326)::geography
                           ) AS distance_m
                    FROM positions
                    WHERE ts BETWEEN %s AND %s
                      AND ST_DWithin(
                              geom,
                              ST_SetSRID(ST_MakePoint(%s, %s), 4326)::geography,
                              %s
                          )
                    ORDER BY distance_m ASC
                    LIMIT 1
                    """,
                    (lon, lat, window_start, window_end, lon, lat, settings.correlation_radius_m),
                )
                row = await cur.fetchone()

            async with conn.cursor() as cur:
                if row is None:
                    await cur.execute(
                        """
                        UPDATE sar_detections
                        SET is_dark = TRUE
                        WHERE id = %s
                        """,
                        (det_id,),
                    )
                    dark += 1
                else:
                    mmsi, ts, distance_m = row
                    await cur.execute(
                        """
                        UPDATE sar_detections
                        SET matched_mmsi = %s,
                            match_distance_m = %s,
                            match_lag_s = EXTRACT(EPOCH FROM (%s::timestamptz - %s::timestamptz)),
                            is_dark = FALSE
                        WHERE id = %s
                        """,
                        (mmsi, distance_m, detected_at, ts, det_id),
                    )
                    matched += 1
        await conn.commit()
    logger.info("correlation pass: matched=%d dark=%d", matched, dark)
    return {"matched": matched, "dark": dark}
