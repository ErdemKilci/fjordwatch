-- V2: refine vessel_anomalies for the API's read patterns.
--
-- Phase 3 introduces /anomalies?since=&min_score=&limit=. The phase 1 schema
-- already indexes (created_at DESC) and (score DESC); this migration adds:
--   * a unique constraint on (mmsi, window_end) so the scheduler can use
--     INSERT ... ON CONFLICT DO NOTHING and we never double-write a window;
--   * a composite index on (created_at DESC, score DESC) for the API's
--     "give me anomalies since X with score >= Y, sorted by recency"
--     scan pattern.

CREATE UNIQUE INDEX IF NOT EXISTS vessel_anomalies_mmsi_window_uidx
    ON vessel_anomalies (mmsi, window_end);

CREATE INDEX IF NOT EXISTS vessel_anomalies_created_score_idx
    ON vessel_anomalies (created_at DESC, score DESC);
