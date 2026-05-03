//! Redis Streams publisher for decoded AIS messages.
//!
//! Each decoded message is appended as a JSON-encoded entry on the
//! `AIS_STREAM` key (default `ais:positions`). Downstream consumers use
//! Redis Streams consumer groups to fan out to the `SignalR` hub and the
//! anomaly detector without coupling to ingestion.

use anyhow::{Context, Result};
use redis::aio::ConnectionManager;
use redis::AsyncCommands;
use tracing::info;

use crate::decoder::DecodedMessage;

const STREAM_MAX_LEN: usize = 100_000;

pub struct RedisStreamPublisher {
    conn: ConnectionManager,
    key: String,
}

impl RedisStreamPublisher {
    pub async fn connect(redis_url: &str, key: &str) -> Result<Self> {
        let client = redis::Client::open(redis_url).context("redis client open")?;
        let conn = ConnectionManager::new(client)
            .await
            .context("redis connection manager")?;
        info!(key, "redis stream publisher connected");
        Ok(Self {
            conn,
            key: key.to_string(),
        })
    }

    pub async fn publish(&mut self, msg: &DecodedMessage) -> Result<()> {
        let payload = serde_json::to_string(msg).context("serialize message")?;
        let _id: String = self
            .conn
            .xadd_maxlen(
                &self.key,
                redis::streams::StreamMaxlen::Approx(STREAM_MAX_LEN),
                "*",
                &[("payload", payload.as_str())],
            )
            .await
            .context("redis xadd")?;
        Ok(())
    }
}
