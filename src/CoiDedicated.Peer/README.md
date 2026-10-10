# Headless gameplay peer harness

This integration harness connects to the gameplay endpoint, authenticates with `join.key`, verifies and loads the server snapshot, and replays authoritative command/tick batches through a separate instance of the actual game simulation. It checks tick continuity and every before/after simulation step, acknowledges applied ticks, and writes selected world counters and terrain fingerprints to `peer-report.json`.

It is a diagnostic peer, not a playable UI client, and the selected counters are not a complete game-state hash.

Final diagnostics also include the exact fixed-point game duration and a `peer-state.raw` serialization produced by the game's `SaveManager.SaveGameRaw` API. A normal save to a discarded stream first invokes the game's `BeforeSave` callbacks, matching the host's capture preparation and populating save-only caches. The report records the raw SHA-256 and length. Identical raw bytes provide stronger evidence; unequal digests alone do not identify a gameplay desynchronization because serialized layout and session-related fields must first be checked.

```powershell
dotnet build src/CoiDedicated.Peer --configuration Release
dotnet run --project src/CoiDedicated.Peer --configuration Release --no-build -- --secret-file server-data/join.key --name peer-one --data-dir peer-one-data --ticks 100 --pause-at 10 --resume-at 20 --pause-at 70
```

Use separate processes and separate data directories for two peers. Proposals use local received-tick counts; all peers observe authoritative command authors/request IDs in their final reports. Pausing the server before collecting final reports permits comparing peers that disconnect at different tick sequence numbers. Run a fresh peer after disconnecting to test snapshot/rejoin behavior.

`--place-machine-at N` performs a bounded, read-only search near an existing entity for an ordinary unlocked machine placement, then proposes normal construction at local tick N. It neither creates a local speculative entity nor requests free construction or validation suppression. This fixture requires a populated island and changes the dedicated island when the server accepts the command.

`--reject-speed-at N` proposes speed 2 at local tick N, which is structurally valid but disallowed by the host's speed-1 policy. This fixture requires one rejection and continued valid tick replay through the end of the session.

Each peer verifies the host's exact three simulation DLL identities before authenticating and stores its verified download under a unique `join-<id>.save` filename. The download is bounded to 256 MiB and SHA-256 checked before game loading. Frame and command readers reject malformed input. Valid rejections of this peer's pending commands are recorded in `commandsRejected` and replay continues; unexpected rejection IDs fail the session. `--timeout-seconds` bounds connection, snapshot receipt, and the replay session; Ctrl+C requests cancellation. Game bootstrap itself is synchronous and finishes or fails before the next cancellation check.

Default endpoint: `127.0.0.1:28551`. `--game-dir` or `COI_GAME_DIR` selects the local game installation. `--ticks`, `--pause-at`, `--resume-at`, and `--name` control the fixture. Use `--help` for all options.
