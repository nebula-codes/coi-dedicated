# Multiplayer synchronization

The revision 5 protocol carries the authoritative speed before each update and
the exact simulation steps before and after it. Both sides disable the game's
CPU-dependent adaptive step budgeting for multiplayer. Speeds 1 through 4 are
accepted; speeds above 4 are rejected. A slow computer slows down or falls
behind rather than silently choosing a different number of simulation steps.

After joining and every 300 authoritative updates (normally 30 seconds), the
server includes a SHA-256 checkpoint. Clients compute the same canonical state
and return their checkpoint tied to that tick and step. The selected state is:

- Every entity's ID, prototype, enabled/paused/destroyed flags, and static center.
- Every product's global quantity, available and unavailable stored amounts,
  and storage capacity, sorted by prototype ID.
- Population, settlement count, homelessness and housing capacity.
- Terrain fingerprint, terrain work queue lengths, and prop counts.
- Simulation step, pause state and speed.

This is **partial divergence detection**, not proof that the complete island is
identical. It does not cover every entity field, research queue, route, vehicle
position, storage buffer or other private state. Raw save bytes are retained in
integration diagnostics but are not treated as a universally canonical digest
across Unity/headless and different runtimes.

A mismatching client freezes its local island and requests recovery. A server
receiving a mismatch sends an explicit fresh-snapshot reload request and closes
that peer. Recovery must terminate the old game scene, authenticate again and
load the server snapshot; continuing the old resolver is unsafe. Other players
continue normally. Network loss, malformed ticks and step mismatches also
freeze the local island instead of running an independent simulation.

Structural train schedule edits are admitted in separate updates so their
indices are revalidated after earlier edits execute. This prevents native
out-of-bounds crashes from stale indices. It does not resolve every concurrent
editing conflict: an index that remains valid can refer to a different item
after another player shifts the list. Stable target identities or versioned
preconditions remain separate work.

With no active players, the server skips checkpoint computation and tick-frame
allocation while still advancing its sequence and simulation. The first update
after a player joins always includes a current checkpoint. No cached or stale
terrain digest is reused. The game's native terrain fingerprint visits every
tile's heights, surface/decal, persisted flags and ordered material layers,
including overflow links; safely making that cheaper needs a separate
incremental-state design.

The headless replay harness supports `--speed-at TICK:SPEED` (repeatable),
`--corrupt-checkpoint` (wire failure fixture), and `--diverge-at TICK` (changes an
entity's pause state locally without a server command). These are opt-in test
tools; run them only against isolated test servers. `--reject-speed-at` now
proposes speed 5 and verifies the policy rejection.

## Validation on the development island

Two native headless peers each completed 1,200 authoritative updates including
speed transitions 1 → 2 → 3 → 4 → 1, pause/resume, building placement at 4× and
speed-5 rejection. Six checkpoints per peer matched the server. Both ended at
step 64,173 with identical 4,667,778-byte raw serializations (SHA-256
`342e2dd3f167f4c20aae642b8ce94390ecf4ae36ecb45b2c1bc392daadd19627`).
A third peer's unauthorized local entity pause was detected at the same step,
while the other peers continued. The revised recovery response passed an
explicit-delivery test, followed by a fresh same-name snapshot join and another
400 updates with three matching checkpoints.

On that 37-entity, 94-person island, the 400-update follow-up averaged 1.05 ms
per native replay update (maximum 76.0 ms). Three full terrain-inclusive
checkpoints averaged 295.4 ms, maximum 296.9 ms. **Checkpoint computation can
cause a visible brief pause** every 30 seconds; incremental terrain checking
is future performance work. These measurements are from headless Windows
processes on one PC, not a Unity rendering benchmark, large-island stress test,
or long-session guarantee. Unity overlay and automatic scene reloading require
an in-game check.

A subsequent revision-5 soak ran two peers for 3,000 updates each (about five
minutes) with 1–4× transitions, pause/resume and building placement. All 11
checkpoints per peer matched the server; both peers ended at step 75,911 with
identical 4,638,631-byte raw serializations (SHA-256
`547e4824dc845746c9a3a6025e9416d8fbe19755126d22fc7aa8b562017b267c`).
Replay averaged 0.47 ms per update, maximum 86.5 ms. Checkpoints averaged
310 ms, maximum 332 ms; the native terrain scan accounted for **99.8%** of that
cost. All other checkpoint work averaged under 0.65 ms, so changing sorting or
small allocations would not remove the pause.

Across 120 samples per peer, private process memory stayed around 1,340 MiB
(maximum 1,348 MiB). One peer ended 3.2 MiB above its first sample and the other
7.3 MiB below. This short observation does not establish long-term memory
stability or large-island capacity. The same complete terrain scan remains in
use for active sessions; the optimization only removes unnecessary work while
no player is connected.
