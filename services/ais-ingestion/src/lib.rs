//! `FjordWatch` AIS ingestion library.
//!
//! Pipeline:
//!
//! ```text
//! source (TCP or replay file) -> decoder (ais crate) -> redis stream
//!                                                    -> postgres writer
//! ```

pub mod config;
pub mod decoder;
pub mod error;
pub mod source;
pub mod store;
pub mod stream;
pub mod telemetry;

use std::sync::Arc;

use anyhow::{Context, Result};
use tokio::sync::mpsc;
use tracing::{error, info};

use crate::{
    config::Config, decoder::DecodedMessage, store::PgWriter, stream::RedisStreamPublisher,
    telemetry::Metrics,
};

const RAW_CHANNEL_CAPACITY: usize = 2048;
const DECODED_CHANNEL_CAPACITY: usize = 4096;

/// Run the ingestion service until any pipeline finishes (i.e. forever for
/// the live source, until EOF for replay) or an error occurs.
pub async fn run(config: Config) -> Result<()> {
    let metrics = Arc::new(Metrics::install().context("install prometheus exporter")?);

    let pg_writer = PgWriter::connect(&config.database_url, config.batch_size)
        .await
        .context("connect to postgres")?;
    let publisher = RedisStreamPublisher::connect(&config.redis_url, &config.stream_key)
        .await
        .context("connect to redis")?;

    let (raw_tx, raw_rx) = mpsc::channel::<String>(RAW_CHANNEL_CAPACITY);
    let (decoded_tx, decoded_rx) = mpsc::channel::<DecodedMessage>(DECODED_CHANNEL_CAPACITY);

    let source_metrics = metrics.clone();
    let source_config = config.clone();
    let source_task = tokio::spawn(async move {
        if let Err(err) = source::run(source_config, raw_tx, source_metrics).await {
            error!(error = %err, "source task terminated");
        }
    });

    let decoder_metrics = metrics.clone();
    let decoder_task = tokio::spawn(async move {
        if let Err(err) = decoder::run(raw_rx, decoded_tx, publisher, decoder_metrics).await {
            error!(error = %err, "decoder task terminated");
        }
    });

    let store_metrics = metrics.clone();
    let store_task = tokio::spawn(async move {
        if let Err(err) = pg_writer.run(decoded_rx, store_metrics).await {
            error!(error = %err, "store task terminated");
        }
    });

    let listen = config.metrics_listen;
    let telemetry_metrics = metrics.clone();
    let telemetry_task = tokio::spawn(async move {
        if let Err(err) = telemetry::serve(telemetry_metrics, listen).await {
            error!(error = %err, "telemetry server terminated");
        }
    });

    tokio::select! {
        _ = source_task => info!("source pipeline ended"),
        _ = decoder_task => info!("decoder pipeline ended"),
        _ = store_task => info!("store pipeline ended"),
        _ = telemetry_task => info!("telemetry server ended"),
    }
    Ok(())
}
