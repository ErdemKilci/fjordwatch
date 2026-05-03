-- V3: refine sar_detections for the dark-vessel overlay queries.
--
-- Phase 4 introduces GET /sar?bbox=&since=&onlyDark=. The phase 1 schema
-- already includes (geom GIST), (detected_at DESC), and (scene_id); this
-- migration adds:
--   * a composite index on (detected_at DESC, is_dark) so the API's
--     "recent dark detections" scan reads sequentially;
--   * a separate index on (matched_mmsi, detected_at) for the join used
--     by the agent (phase 5) when answering "which AIS broadcasts have a
--     SAR sighting near them".

CREATE INDEX IF NOT EXISTS sar_detections_detected_dark_idx
    ON sar_detections (detected_at DESC, is_dark);

CREATE INDEX IF NOT EXISTS sar_detections_matched_mmsi_idx
    ON sar_detections (matched_mmsi, detected_at DESC)
    WHERE matched_mmsi IS NOT NULL;

COMMENT ON COLUMN sar_detections.is_dark IS
    'TRUE when no AIS broadcast was found within CORRELATION_RADIUS_M / CORRELATION_WINDOW_S.';
COMMENT ON COLUMN sar_detections.match_distance_m IS
    'Great-circle distance in meters from the SAR centroid to the matched AIS position.';
COMMENT ON COLUMN sar_detections.match_lag_s IS
    'Detected_at - matched AIS ts, in seconds. Positive when AIS arrived before SAR.';
