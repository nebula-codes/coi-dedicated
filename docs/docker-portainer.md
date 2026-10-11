# Docker / Portainer server and browser manager

The **alpha.6.3 stack** includes the game server and its browser manager in one Linux amd64 container. The manager supervises the server directly and remains available when the island is stopped. It needs no Docker socket, Steam client, Wine, or VNC. The manager requires a private administrator key when accessed over the network. Consult the matching release for publication and validation status; this guide does not establish deployment on your target machine.

The host accepts only the reviewed CoI **0.8.7d** `Mafi`, `Mafi.Core`, and `Mafi.Base` assembly fingerprints. Before each world load it applies a dedicated-process compatibility marker to the game's startup state; it does not initialize Steam or change game files. Unknown game builds stop before island loading rather than receiving an automatic patch. Players still use their normal installed game with the multiplayer client. Player join-key and administrative control-key authentication remain enabled. See the [startup evidence ledger](server-startup-validation.md).

Alpha.6.1 remains the earlier server-only image. Use alpha.6.2 or newer for the browser manager. Do not substitute alpha.5: it lacks the corrected startup compatibility handling and can damage an island with a delayed game-over.

## Portainer stack

Use Docker Standalone on a Linux Intel/AMD 64-bit host. ARM and Docker Swarm are not supported by this stack. Put the complete matching Windows game installation on that Linux host, for example `/srv/coi/game`, preserving filename case. The root executable, Unity files, native libraries, maps, and Managed directory are needed by the game's unchanged file checks. A Windows path on your PC does not refer to a remote Docker host.

Copy [compose.portainer.yml](../compose.portainer.yml) into a Portainer stack. Set these stack environment variables:

```text
COI_IMAGE=ghcr.io/nebula-codes/coi-dedicated:0.1.0-alpha.6.3
COI_GAME_DIR=/srv/coi/game
COI_DATA_VOLUME=coi-dedicated-native-test-data
COI_GAME_PORT=28551
COI_MANAGER_PORT=28560
```

Use the matching image once the alpha.6.3 release confirms publication. `COI_IMAGE` remains mandatory so an old image cannot be selected silently. For an existing stack, keep its exact `COI_DATA_VOLUME` and `COI_GAME_DIR` values; the example test volume above would create a separate island. Back up before changing the stack and do not run two hosts against the same writable volume.

On first manager startup, the server starts automatically: it resumes `/data/island.save` when present, otherwise creates `COI_MAP` (default `New Haven`) with the starting factory enabled. `COI_STARTING_FACTORY=false` disables that factory for a new world. `COI_AUTOSAVE_SECONDS` defaults to 60. Set `COI_MANAGER_AUTOSTART=false` to initially open the manager with the server stopped. Once initialized, dashboard settings and the requested running/stopped state persist across container restarts. Compose map/factory/autosave values seed the initial profile; change saved settings in the dashboard afterward.

The dashboard becomes available before world creation completes. A healthy container means its **manager** is available, including when the island is intentionally stopped. Check the dashboard's game-server status or `/app/control.sh --command status` to confirm that the island has finished loading and can accept players.

## Open the manager

1. Open `http://YOUR-NAS-IP:28560/` (or your chosen `COI_MANAGER_PORT`).
2. In Portainer, open the container console using `/bin/sh`, then run `cat /data/.manager/admin.key`.
3. Copy that administrator key into the dashboard's sign-in form. Keep it private; it controls settings, saves, and downloads. Do not give it to players.

The dashboard provides start, stop, restart, pause, resume, save, live status and player count, logs, backup/download/import/restore, editable island settings, and the player join-key download. One stack manages one island. Game paths and internal ports are fixed by Compose; the settings form keeps them read-only. A custom external `COI_GAME_PORT` is reflected in the player connection details. To use a DNS name instead of the NAS IP, set `COI_MANAGER_HOST` to that exact hostname (without a scheme or port).

The administrator key, browser-session encryption keys, profile, requested running state, runtime metadata, and backups persist under `/data/.manager`. The active island and existing player/control keys stay directly under `/data`; migration does not move or replace them. Sign out when finished on a shared PC. Use the manager on your trusted LAN or private VPN; plain HTTP is not suitable for exposing it directly to the internet. `COI_MANAGER_BIND_IP` can restrict the published dashboard to a specific host interface.

The image runs as UID/GID **1654**. The game directory must be readable by this user. A new named data volume receives the correct owner automatically; prepare bind-mounted data directories for that user. Keep the read-only root filesystem, dropped capabilities, `no-new-privileges`, and non-executable temporary mount from the supplied stack. Memory needs depend on island size; fresh terrain generation can use more memory than an already loaded save. Target-host resource limits still need measurement.

## Update an existing stack

1. Stop the old stack, retain an independent island backup, and record its exact `COI_DATA_VOLUME` and `COI_GAME_DIR` values. If it used Wine, preserve the old Steam profile volume; this update does not delete or modify it.
2. Replace the complete stack YAML, including its image, environment, ports, and volumes. Remove `COI_AUTH_ONLY` from the stack environment (or set it to `false`). A retained `true` value deliberately stops the new container before world loading with an actionable migration message.
3. Remove obsolete `COI_STEAM_VOLUME`, `COI_STEAM_UI_PORT`, Wine settings, `/profile` mount, and desktop port mapping from the stack. The stored Docker profile volume may remain unused for rollback; do not publish it or send it to players.
4. Select the published alpha.6.3 image and update the stack with **pull/re-pull image** enabled. The manager adopts the existing island and keys in place. The same UID 1654 is used by the previous native and Wine images.
5. Update every player to the alpha.6.3 Windows client so the longer join window works for joining players and players already on the island.

No Steam sign-in, Offline Mode, desktop password, or `steam-ui.sh validate` step exists in the native image. Old alpha.6 Wine-specific evidence remains historical; it does not establish that the new native image passed its own tests.

## Controls, player connections, and backups

TCP **28551** is published for players and TCP **28560** for the authenticated browser manager. TCP 28550 remains an internal administrative endpoint. Keep both published ports on a trusted LAN or VPN. The dashboard stays reachable when you click **Stop server**; stop the entire container in Portainer to stop the manager as well.

Open the container console with `/bin/sh` as the default user and use:

```sh
/app/control.sh --command status
/app/control.sh --command pause
/app/control.sh --command resume
/app/control.sh --command save
/app/control.sh --command shutdown
```

The control wrapper reads `/data/control.key` without putting its contents in command-line arguments. The dashboard can download the player join key, or retrieve it in the private console:

```sh
cat /data/join.key
```

Save the complete output as `join.key` on the player's PC and share it privately with the host's LAN/VPN address and player port. Never share `control.key` or the manager's `admin.key`. The player key file appears after world initialization when the dedicated service starts. **Use the alpha.6.3 client for every player.** The wire protocol remains revision 6, so older alpha.6 clients can connect, but their shorter download and no-tick timeouts can interrupt a long join. Game assembly fingerprints must match; alpha.5 clients cannot join. See the [player guide](player-guide.md), [coverage ledger](multiplayer-coverage.md), and [synchronization limits](synchronization.md).

The server allows **600 seconds (ten minutes)** after player authentication to download and load the island and report ready. It pauses simulation during that join. The alpha.6.3 client allows 610 seconds for the join download, including authentication, and its no-tick watchdog allows 615 seconds so already-connected players can wait through the server's loading pause. The server's peer-read timeout is also 615 seconds. These limits are fixed in the release; the manager and Compose do not expose a timeout setting.

The manager receives Docker SIGTERM and gracefully stops its child before exiting. During gameplay the host finishes a final save; during bootstrap it cancels preparation and leaves the previous committed island intact. The stack allows up to 300 seconds before Docker can force termination. Use the dashboard for persistent start/stop choices. Direct control commands remain diagnostic tools and do not change the manager's saved requested state.

Back up the data volume independently. It contains `island.save`, its previous generation, and both keys. Snapshot downloads are also available with `/app/control.sh --command snapshot --output /data/my-backup.save`. An old paused island resumes simulation when the service starts; startup does not preserve a paused session as an operational stop.

## Optional source build

[compose.portainer.build.yml](../compose.portainer.build.yml) takes a reviewed source checkout or Git URL in `COI_BUILD_CONTEXT` plus:

- `COI_SERVER_VERSION`: matching Linux package version, for example `0.1.0-alpha.6.3`.
- `COI_SERVER_ARCHIVE_URL`: `CoI-Dedicated-Server-<version>-linux-x64.zip` from the matching release.
- `COI_SERVER_ARCHIVE_SHA256`: exact checksum from `SHA256SUMS-linux.txt`.

The Dockerfile verifies the archive checksum, extracts only redistributable server/control/manager applications and notices, and rejects game assemblies, keys, or saves in that payload. It pins the Microsoft ASP.NET 10 runtime image by digest. The build context excludes installed games, local releases, Steam profiles, saves, and keys. The Windows manager ZIP and old alpha.6 Wine image are not inputs to this build.

The Linux package can be produced with `scripts/package-linux-server.ps1 -GameDir <installation>`. It contains this project's applications only; the game installation remains a separate read-only runtime mount. No game binaries are published in the package or image.

## Alpha.6.3 publication

The [alpha.6.3 packages and image are published](https://github.com/nebula-codes/coi-dedicated/releases/tag/v0.1.0-alpha.6.3). [Container run 38173332287](https://github.com/nebula-codes/coi-dedicated/actions/runs/38173332287) passed from source `80909f2d7508eb0c5f7cd4071f7a97ca4d05b592`. Anonymous registry checks verified `:0.1.0-alpha.6.3` and the then-current `:alpha` at `sha256:1b3a57b6cc7c819fbbd63376d4055275f5ba8ecd97814dc9197d4cbcc57587a6`, for Linux amd64. The Linux archive checksum is `42db7537d761d4c46e46a376fbb784502af284526673341cf6fa9540cfadbea6`. Re-pull the image when updating the stack, preserve the existing data volume, and update every player to the matching alpha.6.3 client. Publication does not update an existing NAS deployment automatically.

## Validation scope

Alpha.6.3 passed all 13 protocol checks. A test using the packaged host and two actual TCP peers held one player's readiness for 85 seconds without keepalives from the established player; both peers remained connected, the join barrier released, and both acknowledged the resumed authoritative tick. Separate boundary checks kept a loading peer at an elapsed age of 599 seconds and expired it at 601 seconds. The packaged timing values were verified as 600 seconds for joining, 610 for the client download, and 615 for peer inactivity. These boundary checks used controlled peer ages; they do not establish that a Unity client loaded successfully over a full ten-minute wait.

The Windows server package passed five integration groups under PowerShell 5.1 with its bundled runtime: launcher/dashboard assets, copied-island startup and simulation, checksum-verified live backup, manager termination and reattachment to the same server process, and graceful final save/stop. The packaged client installer, repeat installation, and uninstaller passed against an isolated fake game; all 24 payload manifest hashes matched and original fixture files, the shared loader, and an unrelated mod were preserved.

The alpha.6.3 Docker container passed five copied-island integration groups: authenticated access, pause/resume/save/logs and verified backups, safe import/restore, persisted settings/session/stopped state across container restart, and dashboard restart plus Docker shutdown/restart. The island and keys were preserved, shutdown exited cleanly in 1.12 seconds, logout revoked browser access, and the source fixture remained unchanged. Remote NAS deployment, human Unity gameplay with this release, and multi-hour sessions remain unverified. Check the [alpha.6.3 release notes](https://github.com/nebula-codes/coi-dedicated/blob/main/docs/releases/v0.1.0-alpha.6.3.md) for image and download availability. The alpha.6.2 manager and alpha.6.1 simulation results below remain separate historical evidence.

### Alpha.6.2 manager evidence (historical)

Alpha.6.2 passed 31 HTTP/authentication checks, 14 storage checks, Windows and Linux process-supervision tests, and five isolated Linux manager-lifecycle scenarios. The lifecycle scenarios cover automatic startup, saved start/stop intent across manager recreation, dashboard restart, cancellation before control readiness, and reporting a shutdown deadline failure instead of a clean exit.

The alpha.6.2 container also passed five integration groups against a copied real island. Authentication protected state, actions, and private downloads. Pause, resume, save, logs, checksum-verified live backup/download, import, and stopped-island restore succeeded; replacement of a running island was blocked. Restoring preserved the prior island. Settings, authenticated browser sessions, and an intentional stopped state survived container restart. Dashboard restart and Docker shutdown/restart preserved the island and existing player/control keys; the measured gameplay shutdown took 1.21 seconds and exited 0. Logout revoked browser access, and the original source fixture remained unchanged. Browser sign-in and the dashboard were also checked visually.

A separate alpha.6.2 test observed a real canonicalization child and sent Docker SIGTERM through the manager. The container exited 0 in 0.334 seconds with no remaining process. Both the previously committed island and original source fixture remained byte-for-byte identical. Actual NAS/Portainer deployment, human Unity gameplay against this release, and multi-hour sessions remain unverified.

### Alpha.6.1 simulation evidence (historical)

The following results were established for alpha.6.1 and remain distinct from the alpha.6.2 manager and lifecycle checks above. The gameplay protocol is unchanged.

The native entrypoint shell regression passes path alias creation, unchanged player/control key paths, rejection of a stale authentication-only configuration, direct signal delivery, and preservation of host exit success/failure. Its host is synthetic; it does not claim to test actual game saves.

The native Linux candidate passed the unchanged startup stress harness with a copied year-16 island: 120 ordinary storage plans across entity IDs 100 and 200, followed by 3,100 updates. Population stayed at 97, all 16 original vehicles remained, no vehicle was sent for forced scrap, and there were no pirate callbacks, game-over, or product-destruction errors. The expected dedicated compatibility marker was present and no Steamworks assembly was loaded. This ran with networking disabled, a read-only root, dropped capabilities, and non-executable `/tmp`; the image contains no Wine, Steam client, or Steam-check helper.

A separate native test observed an active canonicalization child, sent Docker SIGTERM, and exited 0 in 0.27 seconds with no remaining container process. The committed island and original fixture remained byte-for-byte identical. Test containers were stopped; private evidence and volumes were retained.

The final native image created a fresh default New Haven island through both bootstrap processes and reached hosting with the complete Windows game folder mounted read-only and no Steam profile. Two unchanged alpha.6 Windows native simulation peers connected through the published Docker player port, each replayed 400 ticks with verified checkpoints, and replicated six pause/resume, speed 2/3/4, and building commands. Their final raw serialized world states matched exactly. These are native simulation tests, not a human Unity gameplay test.

The fresh island was paused and saved at step 3056 with population 90 and 17 entities. Docker stop completed in 0.96 seconds with exit 0. Restart initialized at the exact saved step 3056, preserved population 90 and 17 entities, and then resumed simulation normally. The player join key was unchanged. An unchanged alpha.6 peer rejoined with the same player name, replayed 120 more ticks and verified two checkpoints. The final Docker stop exited 0 in 1.01 seconds; the isolated container is stopped. Actual target NAS/Portainer deployment, multi-hour stability, and human Unity gameplay on that target remain separate checks. See the startup ledger for the final release evidence.

The alpha.6.2 image was published as `ghcr.io/nebula-codes/coi-dedicated:0.1.0-alpha.6.2` and the then-current `:alpha`. [Publication run 38134705013](https://github.com/nebula-codes/coi-dedicated/actions/runs/38134705013) passed from source `df896bd24011d82b49464be3d11db731018dd692`. Publication verified both tags anonymously at `sha256:8aeddc6ca65d0a31954842f211c9d7d039885f3a3111fc8bd7125250a8191966`; the moving `:alpha` tag can change with later releases. The Linux archive checksum was `ebddb25dd6f92a16fef925f420da4a120b6b7245fb8f40665a5dbab753f3c055`. This confirms that release's publication, not deployment on the target NAS.
