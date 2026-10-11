#!/bin/bash
set -euo pipefail
umask 077
fail() { printf 'CoI dedicated: %s\n' "$*" >&2; exit 1; }
for directory in /data /profile; do
    test -d "$directory" && test -w "$directory" || fail "$directory must be writable by UID/GID 1654."
done
for assembly in Mafi Mafi.Core Mafi.Base Facepunch.Steamworks.Win64; do
    test -r "/game/Captain of Industry_Data/Managed/$assembly.dll" || fail "Missing installed game assembly: $assembly.dll"
done
test -r '/game/Captain of Industry_Data/Plugins/x86_64/steam_api64.dll' || fail 'Missing installed steam_api64.dll.'
test ! -e /game/steam_appid.txt || fail 'Remove the application-ID override from the mounted game installation.'
case "${COI_AUTH_ONLY:-true}" in true|false) ;; *) fail 'COI_AUTH_ONLY must be true or false.' ;; esac
map=${COI_MAP:-New Haven}
case "$map" in *'/'*|*'\'*|.|..) fail 'COI_MAP must be a shipped map name.' ;; esac
autosave=${COI_AUTOSAVE_SECONDS:-60}
case "$autosave" in ''|*[!0-9]*) fail 'COI_AUTOSAVE_SECONDS must be an integer from 10 to 999999999.' ;; esac
test "${#autosave}" -le 9 && test "$autosave" -ge 10 || fail 'COI_AUTOSAVE_SECONDS must be from 10 to 999999999.'
case "${COI_STARTING_FACTORY:-true}" in true|false|1|0) ;; *) fail 'COI_STARTING_FACTORY must be true or false.' ;; esac
mkdir -p /profile/logs /profile/.vnc
rm -f /tmp/steam-authenticated
server_pid=''
stopping=0
shutdown() {
    test "$stopping" = 0 || return
    stopping=1
    trap '' TERM INT
    server_result=0
    if test -n "$server_pid"; then
        printf 'CoI dedicated: requesting authenticated save and shutdown; waiting for bootstrap if necessary.\n'
        deadline=$((SECONDS + 240))
        accepted=0
        while kill -0 "$server_pid" 2>/dev/null && test "$SECONDS" -lt "$deadline"; do
            if test "$accepted" = 0 && test -s /data/control.key; then
                if timeout 35 /app/control.sh --name docker-stop --command shutdown >>/profile/logs/shutdown.log 2>&1; then accepted=1; fi
            fi
            sleep 2
        done
        if kill -0 "$server_pid" 2>/dev/null; then
            printf 'CoI dedicated: graceful shutdown timed out; final save is not confirmed. Keep the previous backup.\n' >&2
            wineserver -k || true
            exit 1
        fi
        # The command ACK precedes the host's final save. Only its actual exit
        # status confirms whether that save completed successfully.
        set +e
        wait "$server_pid"
        server_result=$?
        set -e
        server_pid=''
        if test "$server_result" -ne 0; then
            printf 'CoI dedicated: host exited unsuccessfully after shutdown; final save is not confirmed.\n' >&2
        fi
    fi
    # No simulation remains. End only this container's private Wine session.
    wineserver -k >/dev/null 2>&1 || true
    exit "$server_result"
}
trap shutdown TERM INT
Xvfb :99 -screen 0 1280x800x24 -nolisten tcp >/profile/logs/xvfb.log 2>&1 &
openbox >/profile/logs/openbox.log 2>&1 &
printf 'CoI dedicated: initializing the private Wine profile.\n'
timeout 180 wineboot -u >/profile/logs/wineboot.log 2>&1 || fail 'Wine initialization failed; inspect the private wineboot log.'
winecfg -v win10 >/profile/logs/winecfg.log 2>&1
# No web desktop starts until an administrator sets its VNC password privately.
if ! test -s /profile/.vnc/passwd; then
    printf 'CoI dedicated: run /app/steam-ui.sh in the container console to set the VNC password. No island is loaded.\n'
    until test -s /profile/.vnc/passwd; do sleep 2; done
fi
x11vnc -display :99 -localhost -rfbport 5900 -rfbauth /profile/.vnc/passwd -forever -shared -noxdamage >/profile/logs/x11vnc.log 2>&1 &
websockify --web /usr/share/novnc 0.0.0.0:6080 localhost:5900 >/profile/logs/novnc.log 2>&1 &
steam="$WINEPREFIX/drive_c/Steam/Steam.exe"
if ! test -f "$steam"; then
    printf 'CoI dedicated: downloading the official Steam installer into the private profile.\n'
    curl --fail --show-error --location --retry 3 https://cdn.akamai.steamstatic.com/client/installer/SteamSetup.exe -o /profile/SteamSetup.exe
    printf '%s  /profile/SteamSetup.exe\n' '7d3654531c32d941b8cae81c4137fc542172bfa9635f169cb392f245a0a12bcb' | sha256sum --check --strict - >/profile/logs/installer-check.log || fail 'Steam installer checksum changed; review and update the pinned official installer.'
    timeout 180 wine /profile/SteamSetup.exe /S '/D=C:\Steam' >/profile/logs/steam-install.log 2>&1 || fail 'Steam installation failed.'
    test -f "$steam" || fail 'Steam installer did not create C:\Steam\Steam.exe.'
fi
LIBGL_ALWAYS_SOFTWARE=1 wine 'C:\Steam\Steam.exe' >/profile/logs/steam-client.log 2>&1 &
printf 'CoI dedicated: sign into your owning Steam account through the secured desktop and complete Steam Guard there.\n'
printf 'CoI dedicated: waiting for genuine Steam initialization and ownership checks. No island is loaded.\n'
while true; do
    # Native SDK output may contain account identifiers; keep it in the private
    # profile rather than the shared Portainer log. The host repeats this check.
    if timeout 45 wine /app/steam-check/CoiDedicated.SteamCheck.exe --game-dir 'Z:\game' > /profile/logs/steam-check.log 2>&1; then
        touch /tmp/steam-authenticated
        if test "${COI_AUTH_ONLY:-true}" = false; then break; fi
        printf 'CoI dedicated: Steam validation passed; authentication-only mode keeps the island unloaded.\n'
        sleep 60
    else
        rm -f /tmp/steam-authenticated
        sleep 10
    fi
done
set -- wine /app/server/CoiDedicated.exe --game-dir 'Z:\game' --data-dir 'Z:\data' \
    --serve --listen 0.0.0.0 --port 28550 --game-port 28551 \
    --secret-file 'Z:\data\control.key' --join-secret-file 'Z:\data\join.key' \
    --map "$map" --autosave-seconds "$autosave"
case "${COI_STARTING_FACTORY:-true}" in true|1) set -- "$@" --starting-factory ;; esac
printf 'CoI dedicated: verified Steam startup; starting the server. The host validates again before each world load.\n'
# Steam's native SDK also writes identifiers during the host's own validation.
# Keep that output in the private profile, including bootstrap child output.
"$@" >>/profile/logs/server.log 2>&1 &
server_pid=$!
set +e
wait "$server_pid"
result=$?
set -e
server_pid=''
wineserver -k >/dev/null 2>&1 || true
if test "$result" -ne 0; then
    printf 'CoI dedicated: server failed; inspect /profile/logs/server.log privately.\n' >&2
fi
exit "$result"
