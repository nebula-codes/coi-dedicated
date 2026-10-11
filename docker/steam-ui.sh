#!/bin/bash
set -eu
umask 077
# Run in the container console to initialize/rotate the VNC password.
# x11vnc prompts on stdin; never put the password in an argument or env var.
test -w /profile || { echo '/profile must be writable by UID 1654.' >&2; exit 1; }
case "${1:-password}" in
    validate)
        # This is only the administrator's setup gate. The unchanged SDK checks
        # still run in SteamCheck and again in the host before every world load.
        touch /profile/steam-setup-ready
        printf 'Steam setup marked ready. The supervisor will perform genuine startup validation; no check is bypassed.\n'
        exit 0
        ;;
    pause-validation)
        test "${COI_AUTH_ONLY:-true}" = true || {
            printf 'Stop the world and restart with COI_AUTH_ONLY=true before pausing validation.\n' >&2
            exit 1
        }
        rm -f /profile/steam-setup-ready /tmp/steam-authenticated
        printf 'Validation paused for startup. Restart the candidate before changing Steam sign-in/mode, so any in-flight check finishes first. Then finish setup and run validate.\n'
        exit 0
        ;;
    password) ;;
    *) printf 'Usage: /app/steam-ui.sh [password|validate|pause-validation]\n' >&2; exit 1 ;;
esac
mkdir -p /profile/.vnc
chmod 700 /profile/.vnc
exec x11vnc -storepasswd /profile/.vnc/passwd
