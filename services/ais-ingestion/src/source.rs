//! NMEA line source. Either a TCP socket against the live Kystverket feed,
//! or a recorded NMEA file replayed line-by-line.

use std::path::PathBuf;
use std::sync::Arc;
use std::time::Duration;

use anyhow::{Context, Result};
use tokio::fs::File;
use tokio::io::{AsyncBufReadExt, AsyncRead, BufReader, Lines};
use tokio::net::TcpStream;
use tokio::sync::mpsc;
use tokio::time::sleep;
use tracing::{debug, info, warn};

use crate::config::Config;
use crate::telemetry::Metrics;

/// Top-level entry point for the source pipeline.
///
/// If `config.replay_file` is set, lines are read from that file with optional
/// pacing; otherwise the service connects to the live AIS TCP socket and
/// reconnects with exponential backoff on failure.
pub async fn run(config: Config, tx: mpsc::Sender<String>, metrics: Arc<Metrics>) -> Result<()> {
    if let Some(path) = config.replay_file.clone() {
        info!(path = %path.display(), "running in replay mode");
        replay_file(path, config.replay_delay(), tx, metrics).await
    } else {
        info!(host = %config.source_host, port = config.source_port, "running in live TCP mode");
        live_tcp(&config, tx, metrics).await
    }
}

async fn replay_file(
    path: PathBuf,
    delay: Duration,
    tx: mpsc::Sender<String>,
    metrics: Arc<Metrics>,
) -> Result<()> {
    let file = File::open(&path)
        .await
        .with_context(|| format!("open replay file {}", path.display()))?;
    let reader = BufReader::new(file);
    let mut lines = reader.lines();
    forward_lines(&mut lines, delay, &tx, &metrics).await
}

async fn live_tcp(config: &Config, tx: mpsc::Sender<String>, metrics: Arc<Metrics>) -> Result<()> {
    let mut backoff = config.reconnect_initial_backoff();
    let max_backoff = config.reconnect_max_backoff();
    let addr = format!("{}:{}", config.source_host, config.source_port);

    loop {
        match TcpStream::connect(&addr).await {
            Ok(stream) => {
                info!(%addr, "connected to live AIS source");
                metrics.reconnects.increment(1);
                backoff = config.reconnect_initial_backoff();
                let reader = BufReader::new(stream);
                let mut lines = reader.lines();
                if let Err(err) = forward_lines(&mut lines, Duration::ZERO, &tx, &metrics).await {
                    warn!(error = %err, "live source reader ended; will reconnect");
                }
            }
            Err(err) => {
                warn!(error = %err, %addr, ?backoff, "tcp connect failed; backing off");
                metrics.connect_errors.increment(1);
            }
        }

        if tx.is_closed() {
            debug!("downstream closed; stopping live source loop");
            return Ok(());
        }
        sleep(backoff).await;
        backoff = (backoff * 2).min(max_backoff);
    }
}

async fn forward_lines<R: AsyncRead + Unpin>(
    lines: &mut Lines<BufReader<R>>,
    delay: Duration,
    tx: &mpsc::Sender<String>,
    metrics: &Metrics,
) -> Result<()> {
    while let Some(line) = lines.next_line().await.context("read line")? {
        if line.is_empty() {
            continue;
        }
        metrics.lines_in.increment(1);
        if tx.send(line).await.is_err() {
            debug!("decoder channel closed; stopping source");
            break;
        }
        if !delay.is_zero() {
            sleep(delay).await;
        }
    }
    Ok(())
}
