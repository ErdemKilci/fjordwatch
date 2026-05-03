"""Postgres I/O for anomaly detection.

Reads recent positions per vessel for a configurable window, and writes
anomaly results back to the ``vessel_anomalies`` table populated by the
phase 1 schema.
"""

from __future__ import annotations

import json
from collections.abc import AsyncIterator
from datetime import UTC, datetime, timedelta

import pandas as pd
import psycopg
from psycopg.rows import dict_row

from .ensemble import EnsembleResult


async def open_pool(database_url: str) -> psycopg.AsyncConnection:
    """Open a single async connection. The scheduler is single-threaded;
    pooling adds complexity for no benefit at our throughput."""
    return await psycopg.AsyncConnection.connect(database_url, row_factory=dict_row)


async def list_active_vessels(
    conn: psycopg.AsyncConnection,
    *,
    since: datetime,
) -> list[int]:
    """Return MMSIs that have produced at least one position since ``since``."""
    async with conn.cursor() as cur:
        await cur.execute(
            "SELECT DISTINCT mmsi FROM positions WHERE ts >= %s",
            (since,),
        )
        rows = await cur.fetchall()
    return [int(row["mmsi"]) for row in rows]


async def read_window(
    conn: psycopg.AsyncConnection,
    *,
    mmsi: int,
    since: datetime,
) -> pd.DataFrame:
    """Read every position for a single vessel since ``since``.

    Returns an empty frame when the vessel has no positions in the window.
    """
    async with conn.cursor() as cur:
        await cur.execute(
            """
            SELECT
                mmsi, ts,
                ST_X(geom::geometry) AS longitude,
                ST_Y(geom::geometry) AS latitude,
                sog_knots, cog_deg, heading_deg, nav_status, msg_type
            FROM positions
            WHERE mmsi = %s AND ts >= %s
            ORDER BY ts ASC
            """,
            (mmsi, since),
        )
        rows = await cur.fetchall()
    if not rows:
        return pd.DataFrame(
            columns=(
                "mmsi",
                "ts",
                "longitude",
                "latitude",
                "sog_knots",
                "cog_deg",
                "heading_deg",
                "nav_status",
                "msg_type",
            )
        )
    df = pd.DataFrame(rows)
    df["ts"] = pd.to_datetime(df["ts"], utc=True)
    return df


async def write_anomalies(
    conn: psycopg.AsyncConnection,
    *,
    results: list[EnsembleResult],
    window_start: datetime,
    window_end: datetime,
    score_floor: float,
) -> int:
    """Insert anomaly rows above ``score_floor``.

    Returns the number of rows inserted.
    """
    rows_to_insert = [
        (
            r.mmsi,
            window_start,
            window_end,
            r.score,
            r.iso_score,
            r.lstm_score,
            json.dumps(r.contributing),
            json.dumps(r.model_versions),
        )
        for r in results
        if r.score >= score_floor
    ]
    if not rows_to_insert:
        return 0
    async with conn.cursor() as cur:
        await cur.executemany(
            """
            INSERT INTO vessel_anomalies (
                mmsi, window_start, window_end, score, iso_score, lstm_score,
                contributing, model_versions
            ) VALUES (%s, %s, %s, %s, %s, %s, %s::jsonb, %s::jsonb)
            ON CONFLICT DO NOTHING
            """,
            rows_to_insert,
        )
    await conn.commit()
    return len(rows_to_insert)


def utc_now() -> datetime:
    return datetime.now(tz=UTC)


def window_since(now: datetime, *, minutes: int) -> datetime:
    return now - timedelta(minutes=minutes)


async def stream_active_vessel_windows(
    conn: psycopg.AsyncConnection,
    *,
    since: datetime,
) -> AsyncIterator[tuple[int, pd.DataFrame]]:
    """Yield ``(mmsi, frame)`` for every active vessel since ``since``.

    Convenience wrapper that combines :func:`list_active_vessels` and
    :func:`read_window` for the scheduler.
    """
    mmsis = await list_active_vessels(conn, since=since)
    for mmsi in mmsis:
        yield mmsi, await read_window(conn, mmsi=mmsi, since=since)
