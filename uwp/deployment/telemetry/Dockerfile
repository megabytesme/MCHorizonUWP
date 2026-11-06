# Build the official Horizon telemetry server from a pinned upstream revision.
# Only the telemetry crate, its server, and the root workspace manifest are fetched.
FROM debian:bookworm-slim AS source
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates git \
    && rm -rf /var/lib/apt/lists/*
ARG HORIZON_REVISION=f42ef42ef6ceccb3c6a20bea2481d87949a51c81
RUN set -eu; \
    printf '%s' "$HORIZON_REVISION" | grep -Eq '^[0-9a-f]{40}$'; \
    git init /src; \
    git -C /src sparse-checkout init --cone; \
    git -C /src sparse-checkout set crates/horizon-telemetry crates/horizon-telemetry-server; \
    git -C /src remote add origin https://github.com/horizon-rust/horizon.git; \
    git -C /src fetch --filter=blob:none --no-tags --depth=1 origin "$HORIZON_REVISION"; \
    git -C /src checkout --detach FETCH_HEAD; \
    test "$(git -C /src rev-parse HEAD)" = "$HORIZON_REVISION"

FROM rust:1.90-slim-bookworm AS build
WORKDIR /src
COPY --from=source /src/Cargo.toml Cargo.toml
COPY --from=source /src/crates/horizon-telemetry crates/horizon-telemetry
COPY --from=source /src/crates/horizon-telemetry-server crates/horizon-telemetry-server
RUN cargo build --release --locked --manifest-path crates/horizon-telemetry-server/Cargo.toml

FROM debian:bookworm-slim
ARG HORIZON_REVISION=f42ef42ef6ceccb3c6a20bea2481d87949a51c81
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 telemetry \
    && useradd --uid 10001 --gid 10001 --no-create-home --no-log-init --shell /usr/sbin/nologin telemetry \
    && install -d --owner=10001 --group=10001 --mode=0750 /data
COPY --from=build --chown=10001:10001 /src/crates/horizon-telemetry-server/target/release/horizon-telemetry-server /usr/local/bin/horizon-telemetry-server
ENV PORT=8080 \
    HORIZON_TELEMETRY_DB=/data/telemetry.sqlite3 \
    HORIZON_TELEMETRY_TRUST_PROXY=1
USER 10001:10001
EXPOSE 8080
LABEL org.opencontainers.image.source="https://github.com/horizon-rust/horizon" \
    org.opencontainers.image.revision="$HORIZON_REVISION" \
    org.opencontainers.image.description="Horizon opt-in telemetry receiver"
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD ["curl", "--fail", "--silent", "--show-error", "--max-time", "2", "http://127.0.0.1:8080/healthz"]
ENTRYPOINT ["horizon-telemetry-server"]
