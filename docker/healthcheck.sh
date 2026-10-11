#!/bin/sh
set -eu
exec /app/control.sh --name docker-healthcheck --command status
