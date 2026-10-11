# syntax=docker/dockerfile:1
# Experimental authenticated Wine/Steam host. See docs/docker-portainer.md.
# Build context excludes installed games, Steam profiles, saves, and keys.
FROM ubuntu:24.04@sha256:534baea6a22c03a63003dbc8dbe78fe34bc0d7e595d9a9dc9834884ff530eb55 AS download
ARG TARGETARCH
# Required corrected Windows release. Deliberately no old alpha.5 fallback.
ARG SERVER_VERSION
ARG SERVER_ARCHIVE_URL
ARG SERVER_ARCHIVE_SHA256
RUN test "${TARGETARCH}" = amd64 && test -n "${SERVER_VERSION}" \
    && test -n "${SERVER_ARCHIVE_URL}" && test "${#SERVER_ARCHIVE_SHA256}" = 64 \
    && apt-get update && apt-get install -y --no-install-recommends ca-certificates curl unzip \
    && curl --fail --show-error --location --retry 3 "${SERVER_ARCHIVE_URL}" -o /tmp/server.zip \
    && printf '%s  /tmp/server.zip\n' "${SERVER_ARCHIVE_SHA256}" | sha256sum --check --strict - \
    && mkdir /unpacked /payload && unzip -q /tmp/server.zip -d /unpacked \
    && cp -a "/unpacked/CoI-Server-Manager-${SERVER_VERSION}-win-x64/bin/server" /payload/server \
    && cp -a "/unpacked/CoI-Server-Manager-${SERVER_VERSION}-win-x64/bin/control" /payload/control \
    && cp -a "/unpacked/CoI-Server-Manager-${SERVER_VERSION}-win-x64/bin/steam-check" /payload/steam-check \
    && test -f /payload/server/CoiDedicated.exe \
    && test -f /payload/control/CoiDedicated.Control.exe \
    && test -f /payload/steam-check/CoiDedicated.SteamCheck.exe \
    && test -z "$(find /payload -iname 'Mafi*.dll' -o -iname 'UnityEngine*.dll' -o -name '*.key' -o -name '*.save')"

FROM ubuntu:24.04@sha256:534baea6a22c03a63003dbc8dbe78fe34bc0d7e595d9a9dc9834884ff530eb55
ARG DEBIAN_FRONTEND=noninteractive
ARG SERVER_VERSION
ARG WINE_VERSION=11.19~noble-1
LABEL org.opencontainers.image.title="CoI Dedicated authenticated hosting candidate" \
      org.opencontainers.image.description="Experimental Wine/Steam startup; runtime validation required before hosting an island." \
      org.opencontainers.image.source="https://github.com/nebula-codes/coi-dedicated" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="${SERVER_VERSION}"
RUN dpkg --add-architecture i386 && apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://dl.winehq.org/wine-builds/winehq.key -o /etc/apt/keyrings/winehq-archive.key \
    && curl -fsSL https://dl.winehq.org/wine-builds/ubuntu/dists/noble/winehq-noble.sources -o /etc/apt/sources.list.d/winehq-noble.sources \
    && apt-get update && apt-get install -y --install-recommends "winehq-devel=${WINE_VERSION}" \
    && apt-get install -y --no-install-recommends xvfb xauth x11vnc novnc websockify openbox fonts-liberation procps \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --uid 1654 --create-home --shell /bin/bash coi \
    && mkdir /data /profile /game /app \
    && chown coi:coi /data /profile && chmod 0700 /data /profile
COPY --from=download /payload/ /app/
COPY --chmod=0755 docker/entrypoint.sh docker/control.sh docker/healthcheck.sh docker/steam-ui.sh /app/
ENV COI_MAP="New Haven" COI_STARTING_FACTORY="true" COI_AUTOSAVE_SECONDS="60" \
    COI_AUTH_ONLY="true" HOME="/profile" WINEPREFIX="/profile/wine" WINEARCH="win64" \
    WINEDEBUG="-all" WINEDLLOVERRIDES="mshtml=" DISPLAY=":99" \
    DOTNET_EnableDiagnostics="0"
USER coi
WORKDIR /profile
EXPOSE 28551/tcp 6080/tcp
STOPSIGNAL SIGTERM
HEALTHCHECK --interval=30s --timeout=40s --start-period=10m --retries=3 CMD ["/app/healthcheck.sh"]
ENTRYPOINT ["/app/entrypoint.sh"]
