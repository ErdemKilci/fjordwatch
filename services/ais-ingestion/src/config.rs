//! Service configuration sourced from CLI flags and environment variables.

use std::net::SocketAddr;
use std::path::PathBuf;
use std::time::Duration;

use clap::Parser;

/// AIS ingestion service.
#[derive(Debug, Clone, Parser)]
#[command(version, about)]
pub struct Config {
    /// Postgres connection string.
    #[arg(long, env = "DATABASE_URL")]
    pub database_url: String,

    /// Redis URL.
    #[arg(long, env = "REDIS_URL", default_value = "redis://redis:6379/0")]
    pub redis_url: String,

    /// Redis Stream key for decoded positions.
    #[arg(long, env = "AIS_STREAM", default_value = "ais:positions")]
    pub stream_key: String,

    /// Live AIS source host (Kystverket NMEA feed).
    #[arg(long, env = "AIS_SOURCE_HOST", default_value = "153.44.253.27")]
    pub source_host: String,

    /// Live AIS source TCP port.
    #[arg(long, env = "AIS_SOURCE_PORT", default_value_t = 5631)]
    pub source_port: u16,

    /// Path to a recorded NMEA file. When set, the live socket is bypassed.
    #[arg(long, env = "AIS_REPLAY_FILE")]
    pub replay_file: Option<PathBuf>,

    /// When replaying, sleep this many milliseconds between lines (0 = as fast as possible).
    #[arg(long, env = "AIS_REPLAY_DELAY_MS", default_value_t = 0)]
    pub replay_delay_ms: u64,

    /// Postgres batch size for inserts.
    #[arg(long, env = "AIS_BATCH_SIZE", default_value_t = 200)]
    pub batch_size: usize,

    /// Initial reconnect backoff in milliseconds (live mode only).
    #[arg(long, env = "AIS_RECONNECT_INITIAL_BACKOFF_MS", default_value_t = 500)]
    pub reconnect_initial_backoff_ms: u64,

    /// Maximum reconnect backoff in milliseconds (live mode only).
    #[arg(long, env = "AIS_RECONNECT_MAX_BACKOFF_MS", default_value_t = 30_000)]
    pub reconnect_max_backoff_ms: u64,

    /// Listen address for /healthz and /metrics.
    #[arg(long, env = "AIS_METRICS_LISTEN", default_value = "0.0.0.0:9100")]
    pub metrics_listen: SocketAddr,

    /// Tracing filter directive (e.g. "info,sqlx=warn").
    #[arg(long, env = "RUST_LOG", default_value = "info,sqlx=warn,redis=info")]
    pub log_level: String,
}

impl Config {
    pub const fn replay_delay(&self) -> Duration {
        Duration::from_millis(self.replay_delay_ms)
    }

    pub const fn reconnect_initial_backoff(&self) -> Duration {
        Duration::from_millis(self.reconnect_initial_backoff_ms)
    }

    pub const fn reconnect_max_backoff(&self) -> Duration {
        Duration::from_millis(self.reconnect_max_backoff_ms)
    }
}
