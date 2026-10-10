#!/bin/sh
set -eu
# A successful authenticated status response confirms the simulation loop is
# pumping commands, rather than merely checking that a TCP port is open.
exec /app/control.sh --name docker-healthcheck --command status
