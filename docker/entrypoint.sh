#!/bin/sh
set -eu
umask 077
fail() { printf 'CoI dedicated: %s\n' "$*" >&2; exit 1; }
# An old auth-only stack must not silently start a world after changing images.
case "${COI_AUTH_ONLY:-false}" in
    false|'') ;;
    true) fail 'COI_AUTH_ONLY is obsolete. This native server needs no Steam setup. Back up /data, then remove COI_AUTH_ONLY (or set false) to authorize normal island startup.' ;;
    *) fail 'Remove the obsolete COI_AUTH_ONLY setting; only false is accepted for migration.' ;;
esac
for assembly in Mafi Mafi.Core Mafi.Base; do
    test -r "/game/Captain of Industry_Data/Managed/$assembly.dll" \
        || fail "Missing installed game assembly: $assembly.dll. Mount the complete matching Windows game folder at /game, preserving filename case."
done
test -d /data && test -w /data || fail '/data must be writable by UID/GID 1654.'
# Native game integrity checks still read this shipped file even though this
# dedicated host does not initialize the Windows Steam SDK.
test -r '/game/Captain of Industry_Data/Plugins/x86_64/steam_api64.dll' \
    || fail 'Missing steam_api64.dll. Supply the complete Windows game installation, not only Managed.'
map=${COI_MAP:-New Haven}
case "$map" in *'/'*|*'\'*|.|..) fail 'COI_MAP must be a shipped map name.' ;; esac
if ! test -f /data/island.save; then
    test -r "/game/Maps/$map.map" || fail "Map not found: /game/Maps/$map.map"
fi
autosave=${COI_AUTOSAVE_SECONDS:-60}
case "$autosave" in ''|*[!0-9]*) fail 'COI_AUTOSAVE_SECONDS must be an integer from 10 to 999999999.' ;; esac
test "${#autosave}" -le 9 && test "$autosave" -ge 10 || fail 'COI_AUTOSAVE_SECONDS must be from 10 to 999999999.'
case "${COI_STARTING_FACTORY:-true}" in true|false|1|0) ;; *) fail 'COI_STARTING_FACTORY must be true or false.' ;; esac
case "${COI_MANAGER_ENABLED:-true}" in true|false) ;; *) fail 'COI_MANAGER_ENABLED must be true or false.' ;; esac
case "${COI_MANAGER_AUTOSTART:-true}" in true|false) ;; *) fail 'COI_MANAGER_AUTOSTART must be true or false.' ;; esac
if test "${COI_MANAGER_ENABLED:-true}" = true && test "$autosave" -gt 3600; then
    fail 'The browser manager requires COI_AUTOSAVE_SECONDS from 10 to 3600.'
fi

# Some shipped dependency paths contain literal Windows backslashes. Supply
# read-only aliases without editing or copying installed game bytes. A unique
# view also survives a process restart when Docker retains the same /tmp.
game_view=$(mktemp -d /tmp/coi-game.XXXXXX)
for entry in /game/* /game/.[!.]* /game/..?*; do
    test -e "$entry" || continue
    ln -s "$entry" "$game_view/$(basename "$entry")"
done
ln -s '/game/Captain of Industry_Data/Plugins/x86_64/steam_api64.dll' \
    "$game_view/Captain of Industry_Data\\Plugins\\x86_64\\steam_api64.dll"
for managed in '/game/Captain of Industry_Data/Managed/'*.dll; do
    test -f "$managed" || continue
    alias_path=$(printf '%s/Captain of Industry_Data\\Managed\\%s' "$game_view" "$(basename "$managed")")
    ln -s "$managed" "$alias_path"
done
if test "${COI_MANAGER_ENABLED:-true}" = true; then
    set -- dotnet /app/manager/CoiDedicated.Manager.dll --container --repository /app \
        --host-dll /app/server/CoiDedicated.dll --listen 0.0.0.0 --port 28560 \
        --game-dir "$game_view" --server-data-dir /data --data-dir /data/.manager \
        --map "$map" --autosave-seconds "$autosave" --public-game-port "${COI_PUBLIC_GAME_PORT:-28551}"
    if test -n "${COI_MANAGER_HOST:-}"; then set -- "$@" --allowed-host "$COI_MANAGER_HOST"; fi
    case "${COI_STARTING_FACTORY:-true}" in true|1) set -- "$@" --starting-factory ;; *) set -- "$@" --no-starting-factory ;; esac
    if test "${COI_MANAGER_AUTOSTART:-true}" = false; then set -- "$@" --no-autostart; fi
    printf 'CoI dedicated: browser manager on TCP 28560; player TCP 28551.\n'
    printf 'CoI dedicated: read /data/.manager/admin.key privately in the container console to sign in.\n'
    # The manager owns the child lifecycle and saves it when Docker sends SIGTERM.
    exec "$@"
fi
set -- dotnet /app/server/CoiDedicated.dll --game-dir "$game_view" --data-dir /data \
    --serve --listen 0.0.0.0 --port 28550 --game-port 28551 \
    --secret-file /data/control.key --join-secret-file /data/join.key \
    --map "$map" --autosave-seconds "$autosave"
case "${COI_STARTING_FACTORY:-true}" in true|1) set -- "$@" --starting-factory ;; esac
if test -f /data/island.save; then
    printf 'CoI dedicated: resuming /data/island.save.\n'
else
    printf 'CoI dedicated: creating island on %s.\n' "$map"
fi
printf 'CoI dedicated: native Linux host; player TCP 28551; admin stays inside the container.\n'
# The real .NET host receives SIGTERM directly, owns bootstrap cancellation,
# performs its final save, and returns its actual exit status to Docker.
exec "$@"
