#!/bin/bash
set -eu
umask 077
# Run in the container console to initialize/rotate the VNC password.
# x11vnc prompts on stdin; never put the password in an argument or env var.
test -w /profile || { echo '/profile must be writable by UID 1654.' >&2; exit 1; }
mkdir -p /profile/.vnc
chmod 700 /profile/.vnc
exec x11vnc -storepasswd /profile/.vnc/passwd
