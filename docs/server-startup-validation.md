# Server startup validation

Status recorded on **2026-10-10** for Captain of Industry **0.8.7d**. The old **alpha.5 server ZIP and Docker image are affected by a startup defect** and must not be used for hosting. The corrected Windows host is included in **alpha.6**, together with the command expansion and revision 6; every client must update too. The earlier local startup-only correction retained revision 5. No corrected hosting-ready Docker image has been published.

## Incident and cause

A live server logged a native custom game-over, product-destruction error, and pirate-raid message. A client's selected-state checkpoint then failed and caused a fresh-snapshot reconnect. Both players had matching checkpoints before the event. A reconnect can load the server's already-damaged state; successful checkpoint verification after reconnect does not undo the event.

Inspection of the installed game identified a startup prerequisite established by its normal successful Steam validation path. The headless host had omitted that startup sequence. The game's simulation can then register a delayed pirate game-over action. This host integration defect can affect an owner's legitimate game installation; it is not evidence that a player has an unlicensed copy.

An isolated regression using the published alpha.5 host reproduced the event: population fell from 97 to 0 and all 16 original vehicles became pending scrap. These results establish the defect beyond a visual interpolation problem. They do not establish every possible effect on storage or other game state.

## Current source behavior

The host requires **Windows x64**, the owner's installed game files, and **Steam running and signed into an account that owns Captain of Industry**. It does not require Unity, a graphics device, or an open CoI game window.

[NativeSteamSession](../src/CoiDedicated/NativeSteamSession.cs) uses the unchanged Facepunch Steam wrapper and native Steam library shipped with the installation. It calls `SteamClient.Init`, requires both `SteamClient.IsValid` and `SteamApps.IsSubscribed`, and records the normal successful-startup state only after those real calls succeed. The session is held through world initialization and hosting, then shut down on exit. No game assembly is patched and no successful validation result is fabricated.

[GameHost](../src/CoiDedicated/GameHost.cs) runs this validation before creating or loading a world. Missing libraries, failed validation, and unsupported platforms stop startup at that boundary. Application-ID override files are rejected. The experimental Linux amd64 container runs this unchanged Windows validation under Wine alongside a genuine Steam client. It defaults to authentication-only mode. Docker deployment remains pending a complete authenticated runtime and shutdown test; see the Docker guide.

## Evidence and test ledger

| Check | Status and scope |
| --- | --- |
| Shipped startup and simulation prerequisites inspected | Confirmed the omitted successful Steam startup state and native delayed game-over path. Private binary evidence remains outside the repository. |
| Actual Steam runtime probe on Windows x64 / .NET 10 | Passed using the installed wrapper and native library: `IsValid` and `IsSubscribed` were true; normal shutdown completed. |
| Current source, authenticated load and save | Passed on an isolated copied island at step 114859 with 87 entities and 97 people; no simulation steps were advanced. |
| Missing native Steam library | Startup stopped before world initialization, with an actionable error. |
| Published alpha.5 regression | Reproduced delayed game-over and population/vehicle damage on an isolated fixture. |
| Corrected host integration regression | **Passed.** The same 120 native storage placements followed by 3,100 simulation steps kept population at 97, with no pirate callbacks, game-over, or vehicle scrapping. Also passed with the retained alpha.5 protocol/adapter assemblies. |
| Final staged service package | **Passed.** The normal executable entry point completed bootstrap, Steam validation, authenticated endpoint startup, simulation, final save and shutdown using the retained alpha.5 protocol/adapter binaries. A packaging assembly-version mismatch found during rollout was corrected before this test. |
| Corrected server with real game clients | **Pending.** A passing Steam probe or zero-step world load does not establish multiplayer stability. |
| Linux Docker authenticated hosting | **Candidate / not ready to host.** Genuine Steam initialization/subscription and copied-island load passed under Wine; login survived relaunch. Steam process lifetime is unstable, and sustained simulation, two-client replay, shutdown/restart, and target Portainer validation remain pending. Old published images remain affected. |

The executable integration fixture is [CoiDedicated.StartupValidation.Tests](../tests/CoiDedicated.StartupValidation.Tests). Test data, saves, account details, credentials, and decompiled game code are not published. Further tests must keep the original island untouched and record which host binary was used.

## Existing islands

Preserve both the current data directory and any separate pre-incident backups. Startup correction prevents the omitted initialization path; it does not repair population, vehicles, products, or game-over state already written to a save. A recovery candidate must be loaded and checked in isolation before any restore. The client's automatic reconnect is synchronization recovery, not a replacement for a pre-incident backup.
