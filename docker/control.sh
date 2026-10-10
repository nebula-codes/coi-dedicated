#!/bin/sh
set -eu
# Arguments are ordinary Control CLI options, e.g. --command save. No key value
# is placed in the process command line or printed by this wrapper.
exec dotnet /app/control/CoiDedicated.Control.dll \
    --host 127.0.0.1 --port 28550 --secret-file /data/control.key "$@"
