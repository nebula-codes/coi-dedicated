#!/bin/sh
set -eu
# Only the key's path is passed. Keep the administrative endpoint unpublished.
exec dotnet /app/control/CoiDedicated.Control.dll \
    --host 127.0.0.1 --port 28550 --secret-file /data/control.key "$@"
