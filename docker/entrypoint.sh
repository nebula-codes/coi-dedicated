#!/bin/sh
set -eu
umask 077

fail() {
    printf 'CoI dedicated: %s\n' "$*" >&2
    exit 1
}

for assembly in Mafi Mafi.Core Mafi.Base; do
    test -r "/game/Captain of Industry_Data/Managed/${assembly}.dll" \
        || fail "Missing ${assembly}.dll. Mount your complete CoI 0.8.7d game folder at /game (read-only), preserving filename case."
done
test -d /data && test -w /data \
    || fail '/data must be a writable volume owned by UID/GID 1654. See docs/docker-portainer.md.'
test -r '/game/Captain of Industry_Data/Plugins/x86_64/steam_api64.dll' \
    || fail 'Missing steam_api64.dll. Supply the complete Windows game installation, not only its Managed folder.'

map=${COI_MAP:-New Haven}
case "$map" in
    *'/'*|*'\'*|.|..) fail 'COI_MAP must be a shipped map name, without a path or .map suffix.' ;;
esac
if ! test -f /data/island.save; then
    test -r "/game/Maps/${map}.map" || fail "Map not found: /game/Maps/${map}.map"
fi

autosave=${COI_AUTOSAVE_SECONDS:-60}
case "$autosave" in
    ''|*[!0-9]*) fail 'COI_AUTOSAVE_SECONDS must be a whole number of seconds (at least 10).' ;;
esac
# Bounds also avoid shell-dependent overflow and values outside the host's Int32.
test "${#autosave}" -le 9 && test "$autosave" -ge 10 \
    || fail 'COI_AUTOSAVE_SECONDS must be from 10 to 999999999.'

# The Windows game's dependency check uses literal backslashes in several
# relative paths. Linux treats those as filename characters. This temporary
# symlink view lets those checks read the original files without modifying or
# copying any game file; normal paths still traverse the read-only /game mount.
game_view=/tmp/coi-game
mkdir "$game_view"
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

set -- dotnet /app/server/CoiDedicated.dll \
    --game-dir "$game_view" --data-dir /data \
    --serve --listen 0.0.0.0 --port 28550 --game-port 28551 \
    --secret-file /data/control.key --join-secret-file /data/join.key \
    --map "$map" --autosave-seconds "$autosave"
case "${COI_STARTING_FACTORY:-true}" in
    true|1) set -- "$@" --starting-factory ;;
    false|0) ;;
    *) fail 'COI_STARTING_FACTORY must be true or false.' ;;
esac

if test -f /data/island.save; then
    printf 'CoI dedicated: resuming /data/island.save.\n'
else
    printf 'CoI dedicated: creating island on %s.\n' "$map"
fi
printf 'CoI dedicated: player TCP 28551; admin stays inside the container.\n'
exec "$@"
