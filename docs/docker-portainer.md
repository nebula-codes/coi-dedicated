# Native Linux Docker / Portainer hosting

The **alpha.6.1 native release** runs the dedicated simulation directly on Linux amd64 with .NET 10. It needs no Steam client, Wine, VNC, or sign-in profile. Local native-container startup, replay, and save/restart checks below have passed. Consult the matching release for published package and image availability; this guide does not establish deployment on your target machine. Do not substitute the old alpha.5 Linux image: that release lacked the startup compatibility handling and can trigger a delayed game-over.

The host accepts only the reviewed CoI **0.8.7d** `Mafi`, `Mafi.Core`, and `Mafi.Base` assembly fingerprints. Before each world load it applies a dedicated-process compatibility marker to the game's startup state; it does not initialize Steam or change game files. Unknown game builds stop before island loading rather than receiving an automatic patch. Players still use their normal installed game with the multiplayer client. Player join-key and administrative control-key authentication remain enabled. See the [startup evidence ledger](server-startup-validation.md).

The [alpha.6.1 release](https://github.com/nebula-codes/coi-dedicated/releases/tag/v0.1.0-alpha.6.1) and its native Linux image are published. The [publication workflow](https://github.com/nebula-codes/coi-dedicated/actions/runs/38132914298) built from the checksum-verified public Linux archive and passed application, payload-privacy, entrypoint signal, and migration checks. Anonymous registry access was verified for `ghcr.io/nebula-codes/coi-dedicated:0.1.0-alpha.6.1`, digest `sha256:133dcd10e5ce2007f228cb12f3bc69aab1481da096f32c0a8871a98d13d0023c`, from source commit `2095af8d9aa287d5063d134a4b7fc932ab59ef57`.

## Portainer stack

Use Docker Standalone on a Linux Intel/AMD 64-bit host. ARM and Docker Swarm are not supported by this stack. Put the complete matching Windows game installation on that Linux host, for example `/srv/coi/game`, preserving filename case. The root executable, Unity files, native libraries, maps, and Managed directory are needed by the game's unchanged file checks. A Windows path on your PC does not refer to a remote Docker host.

Copy [compose.portainer.yml](../compose.portainer.yml) into a Portainer stack. Set these stack environment variables:

```text
COI_IMAGE=ghcr.io/nebula-codes/coi-dedicated:0.1.0-alpha.6.1
COI_GAME_DIR=/srv/coi/game
COI_DATA_VOLUME=coi-dedicated-native-test-data
COI_GAME_PORT=28551
```

Use the matching published image from the alpha.6.1 release; a local candidate tag is not a remote download. `COI_IMAGE` remains mandatory so an old image cannot be selected silently. Start with a separate data volume and a copied island when validating a new host.

The server starts automatically: it resumes `/data/island.save` when present, otherwise creates `COI_MAP` (default `New Haven`) with the starting factory enabled. `COI_STARTING_FACTORY=false` disables that factory for a new world. `COI_AUTOSAVE_SECONDS` defaults to 60 and must be at least 10. Bootstrap creation and canonicalization finish before the gameplay endpoint opens. A successful `/app/control.sh --command status` or healthy container confirms that the service is ready; the initial launch message alone does not.

The image runs as UID/GID **1654**. The game directory must be readable by this user. A new named data volume receives the correct owner automatically; prepare bind-mounted data directories for that user. Keep the read-only root filesystem, dropped capabilities, `no-new-privileges`, and non-executable temporary mount from the supplied stack. Memory needs depend on island size; fresh terrain generation can use more memory than an already loaded save. Target-host resource limits still need measurement.

## Existing Wine-stack migration

1. Stop the old stack, retain an independent island backup, and record its exact `COI_DATA_VOLUME` value. Preserve the old Steam profile volume; this update does not delete or modify it.
2. Replace the complete stack YAML, including its image, environment, ports, and volumes. Remove `COI_AUTH_ONLY` from the stack environment (or set it to `false`). A retained `true` value deliberately stops the new container before world loading with an actionable migration message.
3. Remove obsolete `COI_STEAM_VOLUME`, `COI_STEAM_UI_PORT`, Wine settings, `/profile` mount, and desktop port mapping from the stack. The stored Docker profile volume may remain unused for rollback; do not publish it or send it to players.
4. For testing, use a new data volume with a backed-up copy of `/data/island.save`, owned by UID 1654, before starting the server. For an approved cutover, reuse the original data volume and keys only after the test succeeds. Never run two hosts against the same writable data volume.

No Steam sign-in, Offline Mode, desktop password, or `steam-ui.sh validate` step exists in the native image. Old alpha.6 Wine-specific evidence remains historical; it does not establish that the new native image passed its own tests.

## Controls, player connections, and backups

Only TCP **28551** is published for players. TCP 28550 remains an internal administrative endpoint. There is no web server-manager dashboard in this image. Keep the player port on a trusted LAN or VPN.

Open the container console with `/bin/sh` as the default user and use:

```sh
/app/control.sh --command status
/app/control.sh --command pause
/app/control.sh --command resume
/app/control.sh --command save
/app/control.sh --command shutdown
```

The control wrapper reads `/data/control.key` without putting its contents in command-line arguments. In the same private console, retrieve the player key:

```sh
cat /data/join.key
```

Save the complete output as `join.key` on the player's PC and share it privately with the host's LAN/VPN address and player port. Never share `control.key`. The key file appears after world initialization when the dedicated service starts. **Alpha.6 clients remain compatible with alpha.6.1 servers**: the wire protocol is still revision 6, and game assembly fingerprints must match. Alpha.5 clients cannot join. See the [player guide](player-guide.md), [coverage ledger](multiplayer-coverage.md), and [synchronization limits](synchronization.md).

The native .NET host receives SIGTERM directly. During gameplay it finishes the final save and returns its actual exit status; during bootstrap it cancels the child and leaves the previous committed island intact. The stack allows up to 300 seconds before Docker can force termination. A successful control shutdown acknowledgement precedes the final save, so verify process exit and retain backups. `restart: unless-stopped` can restart a process exited through the control command; stop the container/stack when you want it to stay stopped.

Back up the data volume independently. It contains `island.save`, its previous generation, and both keys. Snapshot downloads are also available with `/app/control.sh --command snapshot --output /data/my-backup.save`. An old paused island resumes simulation when the service starts; startup does not preserve a paused session as an operational stop.

## Optional source build

[compose.portainer.build.yml](../compose.portainer.build.yml) takes a reviewed source checkout or Git URL in `COI_BUILD_CONTEXT` plus:

- `COI_SERVER_VERSION`: matching Linux package version, for example `0.1.0-alpha.6.1`.
- `COI_SERVER_ARCHIVE_URL`: `CoI-Dedicated-Server-<version>-linux-x64.zip` from the matching release.
- `COI_SERVER_ARCHIVE_SHA256`: exact checksum from `SHA256SUMS-linux.txt`.

The Dockerfile verifies the archive checksum, extracts only redistributable server/control applications and notices, and rejects game assemblies, keys, or saves in that payload. It pins the Microsoft .NET 10 runtime image by digest. The build context excludes installed games, local releases, Steam profiles, saves, and keys. The Windows manager ZIP and old alpha.6 Wine image are not inputs to this build.

The Linux package can be produced with `scripts/package-linux-server.ps1 -GameDir <installation>`. It contains this project's applications only; the game installation remains a separate read-only runtime mount. No game binaries are published in the package or image.

## Validation scope

The native entrypoint shell regression passes path alias creation, unchanged player/control key paths, rejection of a stale authentication-only configuration, direct signal delivery, and preservation of host exit success/failure. Its host is synthetic; it does not claim to test actual game saves.

The native Linux candidate passed the unchanged startup stress harness with a copied year-16 island: 120 ordinary storage plans across entity IDs 100 and 200, followed by 3,100 updates. Population stayed at 97, all 16 original vehicles remained, no vehicle was sent for forced scrap, and there were no pirate callbacks, game-over, or product-destruction errors. The expected dedicated compatibility marker was present and no Steamworks assembly was loaded. This ran with networking disabled, a read-only root, dropped capabilities, and non-executable `/tmp`; the image contains no Wine, Steam client, or Steam-check helper.

A separate native test observed an active canonicalization child, sent Docker SIGTERM, and exited 0 in 0.27 seconds with no remaining container process. The committed island and original fixture remained byte-for-byte identical. Test containers were stopped; private evidence and volumes were retained.

The final native image created a fresh default New Haven island through both bootstrap processes and reached hosting with the complete Windows game folder mounted read-only and no Steam profile. Two unchanged alpha.6 Windows native simulation peers connected through the published Docker player port, each replayed 400 ticks with verified checkpoints, and replicated six pause/resume, speed 2/3/4, and building commands. Their final raw serialized world states matched exactly. These are native simulation tests, not a human Unity gameplay test.

The fresh island was paused and saved at step 3056 with population 90 and 17 entities. Docker stop completed in 0.96 seconds with exit 0. Restart initialized at the exact saved step 3056, preserved population 90 and 17 entities, and then resumed simulation normally. The player join key was unchanged. An unchanged alpha.6 peer rejoined with the same player name, replayed 120 more ticks and verified two checkpoints. The final Docker stop exited 0 in 1.01 seconds; the isolated container is stopped. Actual target NAS/Portainer deployment, multi-hour stability, and human Unity gameplay on that target remain separate checks. See the startup ledger for the final release evidence.
