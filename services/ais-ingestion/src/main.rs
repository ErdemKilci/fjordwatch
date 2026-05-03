//! `FjordWatch` AIS ingestion service entry point.

use anyhow::Result;
use clap::Parser;
use tracing::info;

use ais_ingestion::{config::Config, run};

#[tokio::main]
async fn main() -> Result<()> {
    let config = Config::parse();
    init_tracing(&config.log_level)?;
    info!(
        version = env!("CARGO_PKG_VERSION"),
        "ais-ingestion starting"
    );
    run(config).await
}

fn init_tracing(filter: &str) -> Result<()> {
    use tracing_subscriber::{fmt, layer::SubscriberExt, util::SubscriberInitExt, EnvFilter};

    let env_filter = EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new(filter));
    let fmt_layer = fmt::layer()
        .json()
        .with_target(false)
        .with_current_span(false);

    tracing_subscriber::registry()
        .with(env_filter)
        .with(fmt_layer)
        .try_init()
        .map_err(|err| anyhow::anyhow!("init tracing: {err}"))?;

    Ok(())
}
