#!/bin/bash
set -eu
if test "${COI_AUTH_ONLY:-true}" = true; then
    # Refreshed by a real no-world SteamCheck in the entrypoint loop.
    test -f /tmp/steam-authenticated
    exit
fi
exec /app/control.sh --name docker-healthcheck --command status
