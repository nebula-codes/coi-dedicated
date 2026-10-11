#!/bin/bash
# Isolated supervisor regression. Run only in a disposable test container.
set -euo pipefail
test "${COI_SUPERVISOR_TEST:-}" = 1 || { echo 'Requires COI_SUPERVISOR_TEST=1 in an isolated test container.' >&2; exit 1; }
entrypoint=${1:-/app/entrypoint.sh}
function_source=$(sed -n '/^shutdown() {$/,/^}$/p' "$entrypoint")
test -n "$function_source"
test ! -e /data/control.key || { echo 'Refusing to overwrite an existing control key.' >&2; exit 1; }
printf 'isolated-test-placeholder\n' >/data/control.key
trap 'rm -f /data/control.key' EXIT
mkdir -p /profile/logs
run_case() (
    eval "$function_source"
    stopping=0
    server_pid=''
    # Simulate a successful command acknowledgement. The actual child exit
    # happens after the ACK, reproducing a final-save failure in the host.
    timeout() { printf 'mock ACK\n'; return 0; }
    wineserver() { return 0; }
    if test "$1" != none; then
        (sleep 0.2; exit "$1") &
        server_pid=$!
    fi
    shutdown
)
for expected in 0 1; do
    set +e
    run_case "$expected" >"/tmp/shutdown-$expected.log" 2>&1
    actual=$?
    set -e
    test "$actual" = "$expected" || { cat "/tmp/shutdown-$expected.log"; exit 1; }
done
grep -q 'mock ACK' /profile/logs/shutdown.log
grep -q 'final save is not confirmed' /tmp/shutdown-1.log
run_case none >/tmp/shutdown-no-host.log 2>&1
printf 'PASS: ACK then successful host exit; ACK then failed final-save exit; no-world shutdown.\n'
