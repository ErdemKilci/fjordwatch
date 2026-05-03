# syntax=docker/dockerfile:1.7
# ---- builder ---------------------------------------------------------------
FROM rust:1.84-slim-bookworm AS builder
ENV CARGO_TERM_COLOR=always
WORKDIR /workspace

RUN apt-get update \
 && apt-get install -y --no-install-recommends pkg-config libssl-dev \
 && rm -rf /var/lib/apt/lists/*

# Copy manifest first so cargo can cache the dependency build layer.
COPY Cargo.toml Cargo.lock* ./
RUN mkdir -p src \
 && echo 'fn main() {}' > src/main.rs \
 && echo '' > src/lib.rs \
 && cargo build --release \
 && rm -rf src target/release/deps/ais_ingestion*

COPY src ./src
COPY tests ./tests
RUN cargo build --release \
 && strip target/release/ais-ingestion

# ---- runtime ---------------------------------------------------------------
FROM debian:bookworm-slim AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd --system --gid 10001 app \
 && useradd  --system --uid 10001 --gid app --no-create-home --shell /usr/sbin/nologin app

COPY --from=builder /workspace/target/release/ais-ingestion /usr/local/bin/ais-ingestion

USER app:app
EXPOSE 9100
ENV RUST_LOG=info,sqlx=warn

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --silent --fail http://127.0.0.1:9100/healthz || exit 1

ENTRYPOINT ["/usr/local/bin/ais-ingestion"]
