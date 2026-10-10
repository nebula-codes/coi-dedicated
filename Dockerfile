# syntax=docker/dockerfile:1
# This build context contains no game installation or private server data.
# Compile the small redistributable package locally with package-linux-server.ps1.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS download
ARG TARGETARCH
ARG SERVER_VERSION=0.1.0-alpha.5
ARG SERVER_ARCHIVE_SHA256=4ea5037a09c0cb06ff08cebd327789e0e041900efdc4ba2d65854d382c379501
ARG SERVER_ARCHIVE_URL=https://github.com/nebula-codes/coi-dedicated/releases/download/v0.1.0-alpha.5/CoI-Dedicated-Server-0.1.0-alpha.5-linux-x64.zip
RUN test "${TARGETARCH}" = "amd64" \
    && apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl unzip \
    && rm -rf /var/lib/apt/lists/*
RUN curl --fail --show-error --location --retry 3 "${SERVER_ARCHIVE_URL}" --output /tmp/server.zip \
    && printf '%s  /tmp/server.zip\n' "${SERVER_ARCHIVE_SHA256}" | sha256sum --check --strict - \
    && mkdir /unpacked /payload \
    && unzip -q /tmp/server.zip -d /unpacked \
    && cp -a "/unpacked/CoI-Dedicated-Server-${SERVER_VERSION}-linux-x64/." /payload/ \
    && test -f /payload/server/CoiDedicated.dll \
    && test -f /payload/control/CoiDedicated.Control.dll

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble
ARG SERVER_VERSION=0.1.0-alpha.5
LABEL org.opencontainers.image.title="CoI Dedicated Server" \
      org.opencontainers.image.description="Experimental Captain of Industry dedicated simulation host; supply your own game files." \
      org.opencontainers.image.source="https://github.com/nebula-codes/coi-dedicated" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="${SERVER_VERSION}"
WORKDIR /app
COPY --from=download /payload/ /app/
COPY --chmod=0755 docker/entrypoint.sh docker/control.sh docker/healthcheck.sh /app/
# A new Docker named volume inherits this directory's ownership. Bind mounts
# must be prepared for UID/GID 1654 on the Docker host before deployment.
RUN mkdir /data /game && chown app:app /data && chmod 0700 /data
ENV COI_MAP="New Haven" \
    COI_STARTING_FACTORY="true" \
    COI_AUTOSAVE_SECONDS="60" \
    HOME="/data" \
    XDG_CONFIG_HOME="/data/.config" \
    XDG_DATA_HOME="/data/.local/share" \
    DOTNET_EnableDiagnostics="0"
USER app
EXPOSE 28551/tcp
STOPSIGNAL SIGTERM
HEALTHCHECK --interval=30s --timeout=35s --start-period=5m --retries=3 CMD ["/app/healthcheck.sh"]
ENTRYPOINT ["/app/entrypoint.sh"]
