# syntax=docker/dockerfile:1
# Native Linux server. The build context never includes game files or private data.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble@sha256:b89586dc17781f25531909993658aa8161205ae38b8cec8847df4a8221a403d5 AS download
ARG TARGETARCH
ARG SERVER_VERSION
ARG SERVER_ARCHIVE_URL
ARG SERVER_ARCHIVE_SHA256
RUN test "${TARGETARCH}" = amd64 && test -n "${SERVER_VERSION}" \
    && test -n "${SERVER_ARCHIVE_URL}" && test "${#SERVER_ARCHIVE_SHA256}" = 64 \
    && apt-get update && apt-get install -y --no-install-recommends ca-certificates curl unzip \
    && curl --fail --show-error --location --retry 3 "${SERVER_ARCHIVE_URL}" -o /tmp/server.zip \
    && printf '%s  /tmp/server.zip\n' "${SERVER_ARCHIVE_SHA256}" | sha256sum --check --strict - \
    && mkdir /unpacked /payload && unzip -q /tmp/server.zip -d /unpacked \
    && cp -a "/unpacked/CoI-Dedicated-Server-${SERVER_VERSION}-linux-x64/." /payload/ \
    && test -f /payload/server/CoiDedicated.dll \
    && test -f /payload/control/CoiDedicated.Control.dll \
    && test -z "$(find /payload -iname 'Mafi*.dll' -o -iname 'UnityEngine*.dll' -o -name '*.key' -o -name '*.save')"

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble@sha256:b89586dc17781f25531909993658aa8161205ae38b8cec8847df4a8221a403d5
ARG SERVER_VERSION
LABEL org.opencontainers.image.title="CoI Dedicated Server" \
      org.opencontainers.image.description="Experimental native Linux simulation host; supply your own matching game installation." \
      org.opencontainers.image.source="https://github.com/nebula-codes/coi-dedicated" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="${SERVER_VERSION}"
WORKDIR /app
COPY --from=download /payload/ /app/
COPY --chmod=0755 docker/entrypoint.sh docker/control.sh docker/healthcheck.sh /app/
# New named volumes inherit this owner. Prepare bind mounts for UID/GID 1654.
RUN mkdir /data /game && chown app:app /data && chmod 0700 /data
ENV COI_MAP="New Haven" COI_STARTING_FACTORY="true" COI_AUTOSAVE_SECONDS="60" \
    HOME="/data" XDG_CONFIG_HOME="/data/.config" XDG_DATA_HOME="/data/.local/share" \
    DOTNET_EnableDiagnostics="0"
USER app
EXPOSE 28551/tcp
STOPSIGNAL SIGTERM
HEALTHCHECK --interval=30s --timeout=35s --start-period=5m --retries=3 CMD ["/app/healthcheck.sh"]
ENTRYPOINT ["/app/entrypoint.sh"]
