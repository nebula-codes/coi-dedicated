#!/bin/sh
# Shell integration only: substitute a synthetic host, never load game files.
# Actual native save/bootstrap cancellation is tested by test-container*.ps1.
set -eu
test "${COI_SUPERVISOR_TEST:-}" = 1 || { echo 'Requires COI_SUPERVISOR_TEST=1 in an isolated test container.' >&2; exit 1; }
entrypoint=${1:-/app/entrypoint.sh}
fixture=$(mktemp -d /tmp/coi-entrypoint-test.XXXXXX)
active_pid=''
cleanup() {
    if test -n "$active_pid"; then kill "$active_pid" 2>/dev/null || true; wait "$active_pid" 2>/dev/null || true; fi
    rm -rf -- "$fixture"
}
trap cleanup EXIT INT TERM
mkdir -p "$fixture/game/Captain of Industry_Data/Managed" "$fixture/game/Captain of Industry_Data/Plugins/x86_64" "$fixture/game/Maps" "$fixture/data" "$fixture/bin"
for name in Mafi Mafi.Core Mafi.Base; do printf 'fixture' >"$fixture/game/Captain of Industry_Data/Managed/$name.dll"; done
printf 'fixture' >"$fixture/game/Captain of Industry_Data/Plugins/x86_64/steam_api64.dll"
printf 'fixture' >"$fixture/game/Maps/New Haven.map"
# Substitute only fixed mount locations, preserving the actual validation,
# alias construction, command arguments and exec handoff from the shipped script.
sed -e "s|/game|$fixture/game|g" -e "s|/data|$fixture/data|g" "$entrypoint" >"$fixture/entrypoint.sh"
cat >"$fixture/bin/dotnet" <<'HOST'
#!/bin/sh
set -eu
printf '%s\n' "$$" >"$FIXTURE/host.pid"
printf '%s\n' "$@" >"$FIXTURE/host.args"
while test "$#" -gt 0; do
    if test "$1" = --game-dir; then shift; game_view=$1; break; fi
    shift
done
test -r "$game_view/Captain of Industry_Data\\Managed\\Mafi.Core.dll"
test -r "$game_view/Captain of Industry_Data\\Plugins\\x86_64\\steam_api64.dll"
printf '%s\n' "$game_view" >"$FIXTURE/game-view"
if test "${FIXTURE_WAIT:-false}" = true; then
    trap 'printf "synthetic-final-save\n" >"$FIXTURE/signal-observed"; exit "${FIXTURE_EXIT:-0}"' TERM
    printf ready >"$FIXTURE/ready"
    while :; do sleep 0.1; done
fi
exit "${FIXTURE_EXIT:-0}"
HOST
chmod +x "$fixture/bin/dotnet"
export FIXTURE="$fixture" PATH="$fixture/bin:$PATH"
export COI_AUTH_ONLY=false COI_MAP='New Haven' COI_STARTING_FACTORY=true COI_AUTOSAVE_SECONDS=60
for expected in 0 7; do
    set +e
    FIXTURE_EXIT=$expected sh "$fixture/entrypoint.sh" >"$fixture/exit-$expected.log" 2>&1
    actual=$?
    set -e
    test "$actual" = "$expected"
done
grep -qx -- '--join-secret-file' "$fixture/host.args"
grep -qx -- "$fixture/data/join.key" "$fixture/host.args"
grep -qx -- "$fixture/data/control.key" "$fixture/host.args"
# An obsolete auth-only stack must fail before the fake host is even invoked.
rm -f "$fixture/host.pid"
set +e
COI_AUTH_ONLY=true sh "$fixture/entrypoint.sh" >"$fixture/migration.log" 2>&1
actual=$?
set -e
test "$actual" = 1 && test ! -f "$fixture/host.pid"
grep -q 'COI_AUTH_ONLY is obsolete' "$fixture/migration.log"
for expected in 0 7; do
    rm -f "$fixture/ready" "$fixture/signal-observed"
    FIXTURE_WAIT=true FIXTURE_EXIT=$expected sh "$fixture/entrypoint.sh" >"$fixture/signal-$expected.log" 2>&1 &
    active_pid=$!
    count=0
    while ! test -f "$fixture/ready"; do
        kill -0 "$active_pid"
        count=$((count + 1)); test "$count" -lt 100
        sleep 0.05
    done
    # Prove there is no shell supervisor left between Docker's PID and the host.
    test "$(cat "$fixture/host.pid")" = "$active_pid"
    kill -TERM "$active_pid"
    set +e
    wait "$active_pid"
    actual=$?
    set -e
    active_pid=''
    test "$actual" = "$expected"
    test -s "$fixture/signal-observed"
done
printf 'PASS: actual entrypoint aliases and key paths; obsolete setup guard; direct SIGTERM delivery; host success/failure exit status preserved. Synthetic host only.\n'
