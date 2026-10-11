# Server startup validation

Status recorded on **2026-10-11** for **Captain of Industry 0.8.7d**.

## Alpha.6.1 server-only startup

The dedicated server now uses a process-local compatibility adjustment instead of initializing Steam. It runs on Windows x64 and native Linux x64/.NET 10. Docker no longer needs Wine, a Steam account session, a VNC desktop, or a private Steam profile. Players continue launching the normal game with the existing multiplayer client.

[ServerGameCompatibility](../src/CoiDedicated/ServerGameCompatibility.cs) verifies the exact SHA-256 of `Mafi.dll`, `Mafi.Core.dll`, and `Mafi.Base.dll` before the executable creates its data directory or enters bootstrap. [ServerStartup](../src/CoiDedicated/ServerStartup.cs) also checks the files behind the actual loaded assemblies, protecting direct in-process callers from a different preloaded game build. Unsupported or incomplete game updates are rejected before world loading, startup-state mutation, listeners, or save replacement. Assembly version `0.8.7.0` alone is not treated as sufficient identity.

For the reviewed build, the transports initializer tests a specific item in `Mafi.Log.DebugInfos` before registering its delayed ownership-failure callback. Server startup requires the reviewed `ConcurrentBag<string>` layout and inserts that compatibility marker directly. It explicitly reports **server-only compatibility**, never a successful Steam authentication result. The dedicated host does not compile or call `NativeSteamSession`; that file remains only for the separate legacy Steam diagnostic tool.

This changes only the dedicated process. It does not rewrite installed DLLs/executables, fabricate Steam API responses, change ordinary gameplay processors, remove gameplay events, or alter normal client startup. It deliberately removes the server's Steam ownership prerequisite. The complete game installation must still be supplied by the host. Public packages contain no game binaries or private state.

The adjustment is applied on each supported startup, so there is no on-disk patch to lose after a restart. A new game build needs a reviewed compatibility update. No heuristic patching of unknown updates is enabled. Player join-key authentication, administrator control-key authentication, and exact unmodified game fingerprints remain unchanged. **Alpha.6 clients remain compatible** (`dedicated-prototype-6 / cmd1`).

## Current validation

| Check | Result and scope |
| --- | --- |
| Shipped startup behavior | REA identity and retained static/resource analysis match the current Core SHA-256 and MVID. The diagnostic collection and callback branch were inspected. Static inspection has partial metadata coverage and does not establish runtime success by itself. |
| Windows isolated load/save | Passed with the server-only host and an unchanged copied island. |
| Unsupported game updates | Four executable-entry tests passed: one-byte changes to each simulation DLL and a missing Base DLL. Every case exited before bootstrap/lease creation; existing saves and source files stayed byte-for-byte unchanged. |
| Delayed failure regression, Windows | Passed with the final server-only source: 120 ordinary storage placements crossing entity IDs 100 and 200, then 3,100 simulation updates. Population stayed 97, all 16 original vehicles survived without scrap requests, and there were no pirate callbacks, pirate logs, product-destruction errors, or game-over. No Steamworks assembly was loaded. |
| Adapter compatibility | 13 tests passed, including the unchanged revision-6 game fingerprint. |
| Native Linux image smoke | Passed with the actual read-only filesystem, capability restrictions, non-executable temporary mount, and no network. Host/control help, package privacy audit, entrypoint signal/exit tests, and obsolete authentication-mode rejection passed. |
| Native Linux delayed failure regression | Passed the same 120-placement, 3,100-update year-16 fixture with population 97, all 16 original vehicles retained, no scrap requests, pirate callbacks, product-destruction errors, or game-over. No Steamworks assembly was loaded. |
| Native Linux world/replay/lifecycle | Fresh island creation passed both bootstrap phases. Two unchanged alpha.6 headless peers verified 400 updates and six gameplay commands with matching checkpoints and final state. Save and graceful stop passed; restart loaded the exact saved step 3,056 with population 90, 17 entities, and the same join key. A returning peer verified another 120 updates and two checkpoints. Both stops exited zero in about one second. No remote Portainer success is claimed. |
| Native Linux interrupted bootstrap | An actual canonicalization child was interrupted with SIGTERM; the container exited zero in 0.27 seconds with no remaining process. Original and committed save hashes were preserved. |
| Windows packaged manager lifecycle | All five checks passed: the self-contained PowerShell 5.1 launcher without dotnet on PATH, real bootstrap/simulation, byte-verified backup, manager restart and host reattachment, and graceful save/exit. |

The regression fixture is a separate copy of a healthy year-16 island. Its old alpha.5 baseline previously reproduced population loss and scrapping, demonstrating that the fixture reaches the delayed failure condition. Current tests inspect event registration, actual updates, original vehicle identities, and native errors rather than relying only on an early successful load. Test saves, account details, credentials, and decompiled game code remain private.

## Previous incident and authenticated workaround

The published alpha.5 host omitted the startup prerequisite and could arm a delayed pirate game-over. Its regression lost all 97 people and marked all 16 original vehicles for scrap. Those old server assets remain unsuitable for hosting. A startup fix cannot restore damage already saved into an island; use an independent healthy backup.

Alpha.6 performed genuine `SteamClient.Init`, `IsValid`, and `IsSubscribed` checks through the installed Windows wrapper, then recorded the successful startup state. Its Wine/Steam image passed local retained-profile, native-peer, save and restart tests. A remote fresh-island deployment later passed its initial checks and first creation/save phase, then failed the next bootstrap phase with `NoSteamClient: Cannot create IPC pipe`. The supplied log establishes that Steam became unavailable to that process; it does not establish whether Steam exited, disconnected, or experienced an IPC failure.

Alpha.6.1 removes that external Steam-session dependency from the dedicated host. This remains an experimental multiplayer implementation. Remote NAS deployment, human Unity clients against this Linux release, specialized Unity UI paths, and multi-hour sessions still require testing.

See the [Portainer setup and migration guide](docker-portainer.md) and [multiplayer coverage](multiplayer-coverage.md).
