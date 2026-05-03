//! Metrics + HTTP server for /healthz and /metrics.

use std::net::SocketAddr;
use std::sync::Arc;

use anyhow::{Context, Result};
use axum::extract::State;
use axum::http::StatusCode;
use axum::response::IntoResponse;
use axum::routing::get;
use axum::Router;
use metrics::Counter;
use metrics_exporter_prometheus::{PrometheusBuilder, PrometheusHandle};
use tracing::info;

/// Pre-registered counters used throughout the service.
pub struct Metrics {
    pub lines_in: Counter,
    pub decoded: Counter,
    pub partial: Counter,
    pub unsupported: Counter,
    pub decode_errors: Counter,
    pub publish_errors: Counter,
    pub write_errors: Counter,
    pub batches_committed: Counter,
    pub rows_written: Counter,
    pub reconnects: Counter,
    pub connect_errors: Counter,
    pub handle: PrometheusHandle,
}

impl Metrics {
    pub fn install() -> Result<Self> {
        let handle = PrometheusBuilder::new()
            .install_recorder()
            .context("install prometheus recorder")?;
        Ok(Self {
            lines_in: metrics::counter!("ais_lines_in_total"),
            decoded: metrics::counter!("ais_decoded_total"),
            partial: metrics::counter!("ais_partial_total"),
            unsupported: metrics::counter!("ais_unsupported_total"),
            decode_errors: metrics::counter!("ais_decode_errors_total"),
            publish_errors: metrics::counter!("ais_publish_errors_total"),
            write_errors: metrics::counter!("ais_write_errors_total"),
            batches_committed: metrics::counter!("ais_batches_committed_total"),
            rows_written: metrics::counter!("ais_rows_written_total"),
            reconnects: metrics::counter!("ais_source_reconnects_total"),
            connect_errors: metrics::counter!("ais_source_connect_errors_total"),
            handle,
        })
    }
}

pub async fn serve(metrics: Arc<Metrics>, listen: SocketAddr) -> Result<()> {
    let app = Router::new()
        .route("/healthz", get(healthz))
        .route("/readyz", get(readyz))
        .route("/metrics", get(prometheus))
        .with_state(metrics);

    info!(%listen, "telemetry server listening");
    let listener = tokio::net::TcpListener::bind(listen)
        .await
        .with_context(|| format!("bind {listen}"))?;
    axum::serve(listener, app).await.context("axum serve")?;
    Ok(())
}

async fn healthz() -> &'static str {
    "ok"
}

async fn readyz(State(_): State<Arc<Metrics>>) -> impl IntoResponse {
    (StatusCode::OK, "ready")
}

async fn prometheus(State(metrics): State<Arc<Metrics>>) -> impl IntoResponse {
    metrics.handle.render()
}
