#!/bin/sh
set -eu
if test "${COI_MANAGER_ENABLED:-true}" = true; then
    # Stopping the island intentionally must not make its manager unhealthy.
    exec dotnet /app/manager/CoiDedicated.Manager.dll --health-check
fi
exec /app/control.sh --name docker-healthcheck --command status
