# Dedicated game client prototype

This BepInEx 5 Mono plugin connects Captain of Industry **0.8.7d** to the standalone dedicated server. It downloads a verified snapshot, loads it through the normal game loader, and advances the local simulation once per authoritative server tick. The F8 panel also offers a separate paused snapshot preview.

This is experimental, with an explicit command allowlist covering native building batches, truck assignment, research, storage, transport, terrain, roads, and many inspector controls. Unsupported variants fail visibly instead of executing locally; see the [coverage ledger](../../docs/multiplayer-coverage.md). Shared speed is bounded to 1–4. Connection, step, or selected-state failures freeze the island and trigger bounded snapshot recovery when enabled.

## Connect

Install BepInEx 5 x64 Mono, then place `CoiDedicated.Client.dll`, `CoiDedicated.Protocol.dll`, and `CoiDedicated.GameAdapter.dll` together in `BepInEx/plugins/CoiDedicated`. Do not copy game or reference DLLs. Back up any existing installation before replacing files.

From the main menu, press **F8**, enter the server host, port **28551**, and the local path to **join.key**, then choose live join. The paused preview button instead uses control port **28550** and **control.key**. Key contents are never displayed or stored in configuration. Each download creates a unique `Dedicated Snapshot <UTC timestamp>-<random suffix>` save group. Existing saves are not overwritten. Connecting from an ordinary open island is blocked.

Live join persists host, port, normalized key path, and player name to BepInEx configuration before the network attempt. An unavailable server therefore does not discard entered details. The control-preview path is not persisted as the player key. F8 also controls camera/tool-preview sharing and automatic reconnect. Recovery terminates the old scene and reloads a fresh snapshot, with at most three attempts and a retry budget that resets after 60 healthy seconds. Manual disconnect never reconnects automatically.

Optional unattended arguments (autojoin is disabled by default):

```text
--coi-dedicated-auto-join
--coi-dedicated-host=127.0.0.1
--coi-dedicated-port=28551
"--coi-dedicated-key-file=C:\path\to\join.key"
--coi-dedicated-name=test-client
--coi-dedicated-test-ticks=100
--coi-dedicated-test-commands
```

`test-ticks` writes raw serialization and selected counters to `BepInEx/CoiDedicated-diagnostics` after N received ticks, then quits that client. A paused tick may leave the step unchanged. Raw diagnostics first invoke normal `SaveGame(Stream.Null, Gzip, fromTest:true)` to prepare save-time caches. `test-commands` requires at least 80 test ticks and submits pause/resume, a non-machine placement rejected by the server, and an unsupported local command through the actual input scheduler. It checks the original command objects become processed and synced. These options intentionally mutate the test island and are for disposable test sessions.

## Build

```powershell
dotnet build src/CoiDedicated.Client/CoiDedicated.Client.csproj -c Release
```

References use the installed game and official [BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5), published February 8, 2026. Override `GameDir` and `BepInExDir` MSBuild properties if needed. Default references are under `.work/dependencies/BepInEx_win_x64_5.4.23.5/BepInEx/core`. Building does not install anything.

The archive SHA-256 matches the GitHub release API digest:

```text
82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4
```

## Runtime boundaries

- HMAC challenge authentication protects admission. Exact fingerprints of `Mafi.dll`, `Mafi.Core.dll`, and `Mafi.Base.dll`, plus protocol/command version, must match before snapshot transfer. Length, SHA-256, tick sequence, and before/after steps are checked. Live join download has a 610-second deadline, allowing for authentication and the server's 600-second authenticated join window. Separate control-preview downloads retain a 120-second deadline. Both have a 256 MiB limit; failed partial downloads are removed. The live no-tick watchdog allows 615 seconds so connected players can wait through another player's loading pause. Update every client to alpha.6.3 to use these longer limits.
- TCP has no encryption or authenticated server identity. Use localhost or a trusted LAN. The plugin does not open firewall rules.
- Game calls run on Unity's main thread. Background simulation and CPU-dependent adaptive step budgeting are disabled. Each server tick admits one 100 ms game update at the authoritative speed (1–4). Input/render updates continue between ticks. Local commands wait for server broadcast and retain their original objects for UI completion. `CommandRejected` fails the matching pending original while tick reception continues; generic errors remain fatal.
- Selected-state checkpoints run after joining and every 300 server updates. F8 shows the latest verified checkpoint. This is partial divergence detection; see [the precise coverage and limits](../../docs/synchronization.md).
- Normal save checksum, mod checks, terrain generation, and renderer initialization remain active. A narrow Harmony patch on the two `BlobReader` type-name readers maps .NET's `System.Private.CoreLib` identity to Mono's core library only while this plugin loads a dedicated snapshot. The CLR parses nested generic arguments. Snapshot bytes and integrity checks are unchanged.
- Paused preview has a persistent unsynchronized banner and pause guard enabled after initialization. Any preview edits have no effect on the server.
- Matching DLCs/mods remain necessary. Logs are `BepInEx/LogOutput.log` and flushed `BepInEx/coi-client-game-<UTC>.log`.

## Validation

```powershell
dotnet build src/CoiDedicated.Client/Tests/SnapshotSmoke.csproj -c Release
node src/CoiDedicated.Client/Tests/snapshot-smoke.mjs
node src/CoiDedicated.Client/Tests/live-smoke.mjs
```

Five independent Node fixture scenarios exercise the actual transport source: HMAC, exact snapshot bytes, corrupt digest rejection/cleanup, live readiness, proposals, rejection while ticks continue, acknowledgements, sequence-gap rejection, and mismatched assembly identity rejection before transfer.

Actual Unity 6000.3 Mono runtime testing has loaded the server snapshot, initialized renderers, joined alongside two headless peers, and replayed 100 authoritative ticks including another peer's machine placement and pause/resume. All measured final step, duration, entity count, population, and terrain fingerprint matched. These selected measurements do not establish complete state equality: raw serialization differs across save lifecycle/runtime, and further deterministic/UI coverage is required.

A second actual Unity run completed another 100 ticks and all six command checks: four pause/resume requests, a server-rejected non-machine placement, and an unsupported local command. Original input objects became processed and synced, and both rejection cases allowed subsequent ticks to continue. Its final diagnostic includes normal pre-save preparation and is preserved under `.work/client-runtime/unity-command-test-step1612`.

Those Unity runs predate alpha.5. The new reconnect scene flow and expanded overlays have compiled against the installed Unity assemblies and passed policy/geometry/transport checks, but still need an actual two-player visual and reconnect test. The saved connection settings test reloads the real BepInEx configuration file and checks that it contains no key contents.
