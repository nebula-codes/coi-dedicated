# Multiplayer functionality coverage

Alpha.6 targets CoI **0.8.7d**, compatibility **dedicated-prototype-6 / cmd1**. It is not full gameplay parity. Earlier clients cannot join a prototype 6 server. Upgrade every client and server together.

The adapter now covers **286 command classes through 287 wire schemas** (the configured-building variant has its own schema). Static inspection found **328 concrete game commands**, with **282** observed constructor callers in Unity code; these totals also include UI-only, developer, and internal actions. They are not a completion percentage. The complete machine-readable ledger is [multiplayer-command-coverage.json](https://github.com/nebula-codes/coi-dedicated/blob/codex/gameplay-and-presence/research/multiplayer-command-coverage.json). It distinguishes implemented codecs, native processor fixtures, and replay evidence; All 87 previously missing UI-observed command classes now have explicit codecs. Schema coverage does not establish every UI variant or complete gameplay parity.

## Implemented paths

- Native building-menu batches for ordinary player-buildable layout buildings, up to 128 items. Normal construction costs, unlock checks, placement validation, mini-zippers, result objects, and callback timing are preserved.
- Typed copied settings for ordinary machines/storage: recipes, products, storage percentages/alerts/truck groups, pause, logistics, priorities and titles. Unknown fields and external assignment graphs fail explicitly. Mixed complex blueprints and specialized configurations are not accepted as opaque game blobs.
- Ruins scrapping, fleet crew loading/unloading, tree harvesting selections, vehicle navigation, and entity pause toggles.
- Inspector add/remove vehicle controls, including assigning trucks to tree harvesters, preserve the selected vehicle type, count (up to 128), and optional logistics zone. Native game logic chooses available compatible vehicles. Individual vehicle assignment/unassignment remains supported.
- Belt/pipe transport construction schema; mining/dumping designations and removals; mining/forestry work areas; dump-product selection.
- Road/surface designation add/remove, decal add/remove/color, bridge plans/lane settings, rail plans, logistics zone lifecycle and truck work rules. Rail support geometry is rebuilt from trusted prototypes and current terrain.
- Train line/network naming and lifecycle, schedules and station membership, departure conditions, waypoint restrictions, wagon filters, train construction/repair/recovery, track directions/blocks/pillars, colors and icons. These schemas do not imply every rail shape, running-train configuration, or schedule variant has been played through.
- Research queue/start/stop, edicts, contracts/trading, shipyard repair, crop schedule changes, storage products/alerts/logistics/sliders, depot build queues, assignments, priorities, naming, and many scalar factory/vehicle/ship controls. A schema being implemented does not mean every UI variant is tested.
- Camera focus labels including off-screen locations, approximate camera view rectangles, and outlines for buildings, transports, rail plans, paving and terrain tools. These are cosmetic state, separate from authoritative simulation commands. F8 provides a player name and show/share toggle.
- Shared speed 1–4 with fixed native step budgeting, selected-state checkpoints and bounded automatic fresh-snapshot recovery. F8 remembers connection details across game/PC restarts; key contents are never copied into settings.

- Cut/paste selections of up to 128 ordinary machines/storage buildings whose actual copied settings pass the typed paste schema. Transport cuts, mixed selections with unsupported buildings, and unsupported configuration fields are rejected before any source is removed. Replacement and Unity quick actions, bulk road/decal tools, stacker controls, notification dismissal, pinned-product order, difficulty choices and space-program commands.
- Cargo/ship fuel and contracts, livestock and offices, datacenters and nuclear settings, vehicle replacement, world-map fleet/loan/settlement controls. Sandbox storage cheats and instant terrain fulfillment require the authoritative island's sandbox mode.

## Validation completed

- Alpha.6 new-command suite: all 87 newly added schemas have wire fixtures; 1,701 truncated/trailing packets, 48 invalid-value fixtures, and two crafted cut packets are rejected. Native fixtures produced successful results for 81 command classes, expected game prerequisite errors for 4, and wire-only coverage for 2 (cargo-depot contract assignment and ocean access recovery). Asteroid drop testing checks entry into the native processing state, not completion of terrain impact.
- Native cut/paste regression preserves copied storage settings through the actual paste batch. Unsupported mixed selections, configuration fields, and transport cuts leave their sources intact; cut admission is isolated so an earlier command cannot invalidate its preflight within the same tick.
- Alpha.6 startup regression preserved the 97-person island through 120 storage placements and 3,100 further updates. Two revision 6 headless peers each replayed 400 updates with two matching checkpoints and identical final raw simulation state, including shared speed/pause changes and placement. These replay checks do not individually cover all 87 new command classes.

- Unit checks cover explicit wire fixtures, bounds, truncation, invalid versions/booleans/UTF-8, unknown kinds, invalid plans, typed references, and prohibited placement flags.
- A copied island accepted native batch placements for nine prototype families: ordinary machine, unit/loose/fluid storage, research lab, vehicle depot, farm, mining tower, and forestry tower. Duplicate placement and original native completion timing were checked.
- Native processor tests exercised the six originally blocked early-game actions plus storage, planning, farm, work-area, and terrain controls. The JSON ledger distinguishes these from untested schemas.
- A copied live island passed eight truck-assignment scenarios using an existing tree harvester. Tests checked actual two-truck add/remove ownership links, direct individual assignment/unassignment, zone-filtered selection, native zone release behavior, preserved large zone IDs, and rejection of invalid entities/prototypes.
- Two separate simulation peers replayed 100 shared ticks including a native batch. Both ended at step 27812, sequence 1176, with identical serialized world-state SHA-256 `aafe8e2e3b6fb6e3da9ae7264aeb192dad31e498cc3eb0004641a5fed4bc69f1`.
- A two-peer network test verified camera/outline relay, hidden presence, disconnect removal, and three-second expiration while ticks continued. Protocol tests reject nonfinite coordinates, excessive previews, malformed frames, and invalid identities.
- The new copied-island fixture passed 60 native command invocations spanning roads/decals, zones/truck rules, storage/farm/forestry/depot controls, one bridge entrance and lane clearing, one unfinished ground rail with direction preserved, and train line/network/schedule editing, including same-tick admission regressions.
- A separate native blueprint fixture passed storage and machine placement with copied settings, existing-building identity behavior, malformed/truncated payload rejection and unsupported-configuration rejection.
- Two native peers each replayed 1,200 authoritative updates, six matching checkpoints, speed transitions 1–4, pause/resume and building at 4×. Final raw serializations matched. Actual local entity-state divergence was detected; fresh same-name snapshot rejoin passed another 400 updates. See [synchronization measurements](https://github.com/nebula-codes/coi-dedicated/blob/v0.1.0-alpha.5/docs/synchronization.md) for scope and performance.
- A further five-minute revision-5 soak passed 3,000 updates and 11 checkpoints per peer, with matching final raw serialization. A dormant-session regression verifies that a server with no active players skips terrain hashes and network frame construction while advancing its authoritative sequence.
- Actual BepInEx config save/reload, bounded reconnect policy, and five presence geometry/installed-field contract checks pass. These checks do not execute Unity scene transitions or prove the overlay looks correct in-game.

These are bounded fixture results, not proof of long-session determinism. Actual Unity rendering and two people using all menus remain to be tested for this update. Building these sources does not automatically upgrade an existing server or installed client.

## Remaining work

Remaining gaps include mixed/specialized blueprints and external configuration graphs, plus untested UI variants of otherwise implemented commands. Elevated/electrified rails and multi-segment bridge plans have codecs but still need native/in-game variant tests. Speeds above 4 are intentionally rejected.

Checkpoints cover selected canonical fields, not every queue, route, buffer or private entity state. The terrain scan can briefly pause play; large-island and multi-hour sessions need further measurement. Automatic Unity scene reloading and two-player overlay visuals require an actual in-game check.

Presence shows outlines, not textured 3D ghosts. It includes up to 32 previews per player, eight players, and five updates per second. Removal tools, rail-junction overlays and copied graphs outside active tools are not represented. Camera view rectangles use an approximate horizontal plane.

## Command audit

No command class in the current 282-command UI-observed inventory lacks a codec. The ledger records native success fixtures separately from expected-error paths and wire-only tests. Specialized copied configuration graphs remain explicitly limited, even when the command itself is supported.
