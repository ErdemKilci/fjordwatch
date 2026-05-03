-- FjordWatch initial schema.
-- Owner: ais-ingestion (writer), core-api / anomaly-detection (readers).
--
-- Conventions:
--   * geom columns use SRID 4326 (WGS84).
--   * timestamps are TIMESTAMPTZ in UTC.
--   * mmsi is BIGINT to safely span the full 9-digit range.
--   * SQL identifiers are lowercase snake_case.

CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
-- pgvector is added in phase 5 when the RAG corpus is introduced.

-- ----------------------------------------------------------------------------
-- Static / slow-changing vessel facts.
-- ----------------------------------------------------------------------------
CREATE TABLE vessels (
    mmsi              BIGINT       PRIMARY KEY,
    name              TEXT,
    call_sign         TEXT,
    imo               BIGINT,
    ship_type         SMALLINT,
    dim_to_bow        SMALLINT,
    dim_to_stern      SMALLINT,
    dim_to_port       SMALLINT,
    dim_to_starboard  SMALLINT,
    destination       TEXT,
    eta               TIMESTAMPTZ,
    draught_m         REAL,
    first_seen        TIMESTAMPTZ  NOT NULL DEFAULT now(),
    last_seen         TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX vessels_last_seen_idx  ON vessels (last_seen DESC);
CREATE INDEX vessels_ship_type_idx  ON vessels (ship_type);

-- ----------------------------------------------------------------------------
-- Position fixes (high-volume time-series).
-- ----------------------------------------------------------------------------
CREATE TABLE positions (
    mmsi              BIGINT       NOT NULL,
    ts                TIMESTAMPTZ  NOT NULL,
    geom              GEOGRAPHY(POINT, 4326) NOT NULL,
    sog_knots         REAL,                  -- speed over ground
    cog_deg           REAL,                  -- course over ground 0-359.9
    heading_deg       SMALLINT,              -- 0-359 or NULL = not available
    rot_deg_per_min   REAL,                  -- rate of turn
    nav_status        SMALLINT,              -- 0-15 per ITU-R M.1371-5
    msg_type          SMALLINT      NOT NULL,
    PRIMARY KEY (mmsi, ts)
);

CREATE INDEX positions_ts_idx       ON positions (ts DESC);
CREATE INDEX positions_geom_idx     ON positions USING GIST (geom);

-- ----------------------------------------------------------------------------
-- Recent track view: 24 hours of positions per vessel as a LineString.
-- Refreshed periodically by a worker (added in phase 2 alongside the API).
-- ----------------------------------------------------------------------------
CREATE MATERIALIZED VIEW vessel_tracks AS
SELECT
    p.mmsi,
    v.name,
    v.ship_type,
    ST_MakeLine(p.geom::geometry ORDER BY p.ts) AS track,
    MIN(p.ts) AS track_start,
    MAX(p.ts) AS track_end,
    COUNT(*)  AS point_count
FROM positions p
LEFT JOIN vessels v ON v.mmsi = p.mmsi
WHERE p.ts > now() - INTERVAL '24 hours'
GROUP BY p.mmsi, v.name, v.ship_type;

CREATE UNIQUE INDEX vessel_tracks_mmsi_idx ON vessel_tracks (mmsi);

-- ----------------------------------------------------------------------------
-- Stubs for tables that later phases populate. Defined here so foreign keys
-- and grants have a single source of truth.
-- ----------------------------------------------------------------------------
CREATE TABLE vessel_anomalies (
    id              BIGSERIAL    PRIMARY KEY,
    mmsi            BIGINT       NOT NULL REFERENCES vessels(mmsi) ON DELETE CASCADE,
    window_start    TIMESTAMPTZ  NOT NULL,
    window_end      TIMESTAMPTZ  NOT NULL,
    score           REAL         NOT NULL,
    iso_score       REAL,
    lstm_score      REAL,
    contributing    JSONB,
    model_versions  JSONB,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX vessel_anomalies_mmsi_idx       ON vessel_anomalies (mmsi);
CREATE INDEX vessel_anomalies_score_idx      ON vessel_anomalies (score DESC);
CREATE INDEX vessel_anomalies_created_at_idx ON vessel_anomalies (created_at DESC);

CREATE TABLE sar_detections (
    id            BIGSERIAL    PRIMARY KEY,
    scene_id      TEXT         NOT NULL,
    detected_at   TIMESTAMPTZ  NOT NULL,
    geom          GEOGRAPHY(POINT, 4326) NOT NULL,
    bbox_geom     GEOGRAPHY(POLYGON, 4326),
    confidence    REAL         NOT NULL,
    is_dark       BOOLEAN      NOT NULL DEFAULT FALSE,
    matched_mmsi  BIGINT,
    match_distance_m REAL,
    match_lag_s   REAL,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX sar_detections_geom_idx        ON sar_detections USING GIST (geom);
CREATE INDEX sar_detections_detected_at_idx ON sar_detections (detected_at DESC);
CREATE INDEX sar_detections_scene_id_idx    ON sar_detections (scene_id);

-- ----------------------------------------------------------------------------
-- Helper: keep vessels.last_seen in sync on every position insert.
-- ----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION touch_vessel_last_seen() RETURNS trigger AS $$
BEGIN
    UPDATE vessels SET last_seen = NEW.ts WHERE mmsi = NEW.mmsi AND last_seen < NEW.ts;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER positions_touch_vessel
AFTER INSERT ON positions
FOR EACH ROW
EXECUTE FUNCTION touch_vessel_last_seen();
