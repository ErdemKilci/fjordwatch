//! Postgres writer. Buffers decoded messages in batches and flushes them
//! transactionally with `INSERT ... ON CONFLICT` upserts.

use std::sync::Arc;
use std::time::Duration;

use anyhow::{Context, Result};
use sqlx::postgres::{PgPool, PgPoolOptions};
use sqlx::Postgres;
use tokio::sync::mpsc;
use tokio::time;
use tracing::{debug, error, info, warn};

use crate::decoder::{DecodedMessage, Position};
use crate::telemetry::Metrics;

const FLUSH_INTERVAL: Duration = Duration::from_millis(500);

#[derive(Clone)]
pub struct PgWriter {
    pool: PgPool,
    batch_size: usize,
}

impl PgWriter {
    pub async fn connect(database_url: &str, batch_size: usize) -> Result<Self> {
        let pool = PgPoolOptions::new()
            .max_connections(8)
            .acquire_timeout(Duration::from_secs(10))
            .connect(database_url)
            .await
            .context("postgres pool connect")?;
        Self::ping(&pool).await?;
        info!("postgres connection established");
        Ok(Self { pool, batch_size })
    }

    async fn ping(pool: &PgPool) -> Result<()> {
        sqlx::query("SELECT 1")
            .execute(pool)
            .await
            .context("postgres ping")?;
        Ok(())
    }

    /// Run the writer until `rx` is closed.
    pub async fn run(
        self,
        mut rx: mpsc::Receiver<DecodedMessage>,
        metrics: Arc<Metrics>,
    ) -> Result<()> {
        let mut buffer: Vec<DecodedMessage> = Vec::with_capacity(self.batch_size);
        let mut interval = time::interval(FLUSH_INTERVAL);
        interval.set_missed_tick_behavior(time::MissedTickBehavior::Delay);

        loop {
            tokio::select! {
                msg = rx.recv() => {
                    let Some(msg) = msg else {
                        debug!("decoder channel closed; flushing tail and exiting");
                        if !buffer.is_empty() {
                            if let Err(err) = self.flush(&mut buffer, &metrics).await {
                                error!(error = %err, "tail flush failed");
                            }
                        }
                        return Ok(());
                    };
                    buffer.push(msg);
                    if buffer.len() >= self.batch_size {
                        if let Err(err) = self.flush(&mut buffer, &metrics).await {
                            error!(error = %err, "flush failed");
                        }
                    }
                },
                _ = interval.tick() => {
                    if !buffer.is_empty() {
                        if let Err(err) = self.flush(&mut buffer, &metrics).await {
                            error!(error = %err, "interval flush failed");
                        }
                    }
                }
            }
        }
    }

    async fn flush(&self, buffer: &mut Vec<DecodedMessage>, metrics: &Metrics) -> Result<()> {
        if buffer.is_empty() {
            return Ok(());
        }
        let count = buffer.len();
        let mut tx = self.pool.begin().await.context("begin tx")?;
        for msg in buffer.drain(..) {
            if let Err(err) = upsert_one(&mut tx, &msg).await {
                warn!(mmsi = msg.mmsi, error = %err, "row write failed; skipping");
                metrics.write_errors.increment(1);
            }
        }
        tx.commit().await.context("commit tx")?;
        metrics.batches_committed.increment(1);
        metrics.rows_written.increment(count as u64);
        debug!(count, "batch committed");
        Ok(())
    }
}

async fn upsert_one(tx: &mut sqlx::Transaction<'_, Postgres>, msg: &DecodedMessage) -> Result<()> {
    upsert_vessel(tx, msg).await?;
    if let Some(pos) = &msg.position {
        insert_position(tx, msg, pos).await?;
    }
    Ok(())
}

async fn upsert_vessel(
    tx: &mut sqlx::Transaction<'_, Postgres>,
    msg: &DecodedMessage,
) -> Result<()> {
    let s = msg.static_data.clone().unwrap_or_default();
    let eta = build_eta(s.eta_month, s.eta_day, s.eta_hour, s.eta_minute);

    sqlx::query(
        r"
        INSERT INTO vessels (
            mmsi, name, call_sign, imo, ship_type,
            dim_to_bow, dim_to_stern, dim_to_port, dim_to_starboard,
            destination, eta, draught_m, last_seen
        ) VALUES (
            $1, $2, $3, $4, $5,
            $6, $7, $8, $9,
            $10, $11, $12, $13
        )
        ON CONFLICT (mmsi) DO UPDATE SET
            name             = COALESCE(EXCLUDED.name, vessels.name),
            call_sign        = COALESCE(EXCLUDED.call_sign, vessels.call_sign),
            imo              = COALESCE(EXCLUDED.imo, vessels.imo),
            ship_type        = COALESCE(EXCLUDED.ship_type, vessels.ship_type),
            dim_to_bow       = COALESCE(EXCLUDED.dim_to_bow, vessels.dim_to_bow),
            dim_to_stern     = COALESCE(EXCLUDED.dim_to_stern, vessels.dim_to_stern),
            dim_to_port      = COALESCE(EXCLUDED.dim_to_port, vessels.dim_to_port),
            dim_to_starboard = COALESCE(EXCLUDED.dim_to_starboard, vessels.dim_to_starboard),
            destination      = COALESCE(EXCLUDED.destination, vessels.destination),
            eta              = COALESCE(EXCLUDED.eta, vessels.eta),
            draught_m        = COALESCE(EXCLUDED.draught_m, vessels.draught_m),
            last_seen        = GREATEST(vessels.last_seen, EXCLUDED.last_seen)
        ",
    )
    .bind(msg.mmsi)
    .bind(s.name)
    .bind(s.call_sign)
    .bind(s.imo.map(i64::from))
    .bind(s.ship_type.map(i16::from))
    .bind(s.dim_to_bow.map(|v| i16::try_from(v).unwrap_or(i16::MAX)))
    .bind(s.dim_to_stern.map(|v| i16::try_from(v).unwrap_or(i16::MAX)))
    .bind(s.dim_to_port.map(|v| i16::try_from(v).unwrap_or(i16::MAX)))
    .bind(
        s.dim_to_starboard
            .map(|v| i16::try_from(v).unwrap_or(i16::MAX)),
    )
    .bind(s.destination)
    .bind(eta)
    .bind(s.draught_m)
    .bind(msg.ts)
    .execute(&mut **tx)
    .await
    .context("upsert vessel")?;
    Ok(())
}

async fn insert_position(
    tx: &mut sqlx::Transaction<'_, Postgres>,
    msg: &DecodedMessage,
    pos: &Position,
) -> Result<()> {
    sqlx::query(
        r"
        INSERT INTO positions (
            mmsi, ts, geom, sog_knots, cog_deg, heading_deg,
            rot_deg_per_min, nav_status, msg_type
        ) VALUES (
            $1, $2, ST_SetSRID(ST_MakePoint($3, $4), 4326)::geography,
            $5, $6, $7, $8, $9, $10
        )
        ON CONFLICT (mmsi, ts) DO NOTHING
        ",
    )
    .bind(msg.mmsi)
    .bind(msg.ts)
    .bind(pos.longitude)
    .bind(pos.latitude)
    .bind(pos.speed_over_ground)
    .bind(pos.course_over_ground)
    .bind(
        pos.true_heading
            .map(|v| i16::try_from(v).unwrap_or(i16::MAX)),
    )
    .bind(pos.rate_of_turn_deg_per_min)
    .bind(pos.navigation_status.map(i16::from))
    .bind(i16::from(msg.message_type))
    .execute(&mut **tx)
    .await
    .context("insert position")?;
    Ok(())
}

/// AIS ETA fields are month/day/hour/minute with no year. We fold them into
/// the next-future occurrence relative to today's UTC date. When any field is
/// missing or the values are outside range, we return `None`.
fn build_eta(
    month: Option<u8>,
    day: Option<u8>,
    hour: Option<u8>,
    minute: Option<u8>,
) -> Option<chrono::DateTime<chrono::Utc>> {
    use chrono::{Datelike, NaiveDate, TimeZone, Utc};
    let (m, d, h, mi) = (month?, day?, hour?, minute?);
    if !(1..=12).contains(&m) || !(1..=31).contains(&d) || h > 23 || mi > 59 {
        return None;
    }
    let today = Utc::now().date_naive();
    let mut year = today.year();
    let candidate = NaiveDate::from_ymd_opt(year, u32::from(m), u32::from(d))?.and_hms_opt(
        u32::from(h),
        u32::from(mi),
        0,
    )?;
    if candidate.date() < today {
        year += 1;
    }
    let final_naive = NaiveDate::from_ymd_opt(year, u32::from(m), u32::from(d))?.and_hms_opt(
        u32::from(h),
        u32::from(mi),
        0,
    )?;
    Utc.from_utc_datetime(&final_naive).into()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn build_eta_rejects_invalid_month() {
        assert!(build_eta(Some(13), Some(1), Some(0), Some(0)).is_none());
    }

    #[test]
    fn build_eta_rejects_missing_field() {
        assert!(build_eta(Some(6), None, Some(12), Some(0)).is_none());
    }

    #[test]
    fn build_eta_accepts_valid_components() {
        let eta = build_eta(Some(6), Some(15), Some(12), Some(30)).expect("valid");
        assert_eq!(eta.format("%m-%d %H:%M").to_string(), "06-15 12:30");
    }
}
