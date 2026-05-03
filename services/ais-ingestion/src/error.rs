//! Service error types.

use thiserror::Error;

#[derive(Error, Debug)]
pub enum IngestError {
    #[error("nmea parse error: {0}")]
    Nmea(String),

    #[error("ais decode error: {0}")]
    Ais(String),

    #[error("redis error: {0}")]
    Redis(#[from] redis::RedisError),

    #[error("database error: {0}")]
    Database(#[from] sqlx::Error),

    #[error("io error: {0}")]
    Io(#[from] std::io::Error),

    #[error("address parse: {0}")]
    Addr(#[from] std::net::AddrParseError),

    #[error("internal: {0}")]
    Other(String),
}

pub type IngestResult<T> = Result<T, IngestError>;
