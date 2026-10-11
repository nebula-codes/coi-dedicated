#!/bin/bash
set -eu
# Only the key's path is passed. Keep the administrative endpoint unpublished.
exec wine /app/control/CoiDedicated.Control.exe \
    --host 127.0.0.1 --port 28550 --secret-file 'Z:\data\control.key' "$@"
