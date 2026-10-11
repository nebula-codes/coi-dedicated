# Authenticated Docker / Portainer hosting candidate

**Deployment of the published alpha.5 Docker image remains paused.** Its missing Steam startup validation can cause a delayed pirate game-over and damage an island. Preserve its data volume and separate backups. Do not resume that image. The corrected native Linux host refuses to load an island because it cannot run the installed Windows Steam wrapper. See the [startup incident and validation ledger](server-startup-validation.md).

The Docker files in the current source are an **experimental replacement**, using the Windows server and genuine Windows Steam client together under Wine. They are not a newly published or validated hosting release. **Authentication-only mode is the default: no island loads.** Real Steam authentication and a copied-island load have passed locally, but the Steam client exits unexpectedly in this Wine environment. Stable simulation, reconnect, and graceful stop/restart tests must pass before this candidate is used for a live island.

## Why this needs a real Steam client

The server calls the game's unchanged Facepunch `SteamClient.Init`, checks `SteamClient.IsValid` and `SteamApps.IsSubscribed`, and stops before world loading on failure. Valve documents that client initialization requires Steam running in the same user context with an account owning the application. [Steamworks initialization](https://partner.steamgames.com/doc/sdk/api), [subscription API](https://partner.steamgames.com/doc/api/ISteamApps).

A generic anonymous game-server login or a connecting player's authentication ticket does not establish this local owning-client session. These APIs have different purposes; this project cannot substitute them for CoI's checks. [Steam game-server API](https://partner.steamgames.com/doc/api/ISteamGameServer), [authentication tickets](https://partner.steamgames.com/doc/features/auth). No application-ID overrides, fabricated subscription responses, copied session tokens, or game patches are used.

## Candidate layout

The image targets Linux **amd64** Docker Standalone, with Windows x64 .NET self-contained binaries and Wine 11.0. Ubuntu's base image digest and Wine package version are pinned in the Dockerfile. The official Steam installer is downloaded into the private profile and checked against a pinned SHA-256; Steam subsequently updates itself normally. A changed installer checksum stops setup until the new official installer is reviewed.

- `/game`: your complete matching Windows CoI 0.8.7d installation, mounted read-only from the Linux host. Game files are never put in the public image or source context.
- `/data`: island, saves, control key, and player join key. Use a separate new volume for candidate testing.
- `/profile`: private Wine prefix, Steam installation and login state, VNC password, and private authentication diagnostics. Keep it separate from shareable island backups. Do not publish, export to friends, or upload this volume.
- TCP 28551: the player endpoint, inactive in authentication-only mode. Keep it on a trusted LAN/VPN.
- TCP 28550: administrative endpoint inside the container only.
- TCP 6080: Steam desktop web interface, published **only on the host's loopback** at port 28680. No server-manager web dashboard is included.

The application runs as UID/GID **1654**, with a read-only root filesystem, dropped capabilities, no privileged mode, and no Docker socket mount. Allow several gigabytes for the Wine image, prefix, and Steam updates, in addition to your game files and island data. The exact long-session resource requirements remain unmeasured.

Docker Desktop on Windows is only a local proof environment. Its Windows bind mounts caused Wine to fail when mapping Windows executable/assembly images. The proof used unchanged files copied into a private native Docker volume; this is not evidence that the game files or .NET needed modification. The deployment target is a Linux host with native filesystem mounts.

## Build inputs and stack

There is deliberately no automatic fallback to the broken alpha.5 image or ZIP. [compose.portainer.yml](../compose.portainer.yml) requires `COI_IMAGE` to name a reviewed candidate image. [compose.portainer.build.yml](../compose.portainer.build.yml) additionally requires:

- `COI_BUILD_CONTEXT`: a reviewed source checkout or Git URL containing this Dockerfile.
- `COI_SERVER_VERSION`, `COI_SERVER_ARCHIVE_URL`, `COI_SERVER_ARCHIVE_SHA256`: a corrected Windows manager archive and its checksum.

That archive must contain `bin/server`, `bin/control`, and `bin/steam-check`. Older manager archives without the new helper are rejected. The build extracts only these redistributable applications, and checks that game assemblies, keys, and saves are absent. It never sends `/game` or the persistent profile to the build context. No corrected downloadable archive is announced by this guide.

Set `COI_GAME_DIR` to the complete installation's absolute path **on the Linux Docker host**, such as `/srv/coi/game`; a Windows PC path does not refer to a remote Portainer host. Preserve filename case and directory structure. Ensure UID 1654 can read it. Set separate test volume names, such as `COI_DATA_VOLUME=coi-auth-test-data` and `COI_STEAM_VOLUME=coi-auth-test-steam-private`.

Keep `COI_AUTH_ONLY=true` for initial setup. This setting remains the default even after successful login. The container will not load an island until it is explicitly changed to `false`; that change must follow runtime validation, not simply the appearance of a Steam window.

## Private Steam sign-in

Portainer's own HTTPS login does **not** secure a different desktop port. Do not change the stack's loopback publication to `0.0.0.0` for convenience. Access the desktop through an SSH tunnel, or an existing correctly authenticated TLS reverse proxy with restricted access. For an SSH-capable Linux host:

```sh
ssh -N -L 28680:127.0.0.1:28680 your-user@your-docker-host
```

In Portainer, open the container's console as its default user and run:

```sh
/app/steam-ui.sh
```

Enter a unique VNC password directly at the prompt. VNC uses only its first eight characters; its password is an additional local access barrier, not transport encryption. The script never accepts the password as an environment variable or command-line argument. The desktop waits until its password file exists.

Open `http://127.0.0.1:28680/vnc.html` on the tunneled computer. Enter the VNC password, then sign into **your account that owns Captain of Industry** in the actual Steam window and complete Steam Guard there. Do not send your Steam password or Guard code to this project or put either in a stack environment variable. Steam remembers its session in the private profile volume; it may require reauthentication after a restart or Steam update.

The no-world `CoiDedicated.SteamCheck` helper performs the same real startup checks used by the server. The public container log reports success or waiting status; native authentication diagnostics stay in `/profile/logs/steam-check.log` because SDK output may include account identifiers. In authentication-only mode, a healthy status means the latest check passed, **not** that gameplay or save handling has been validated.

The host repeats the checks itself before each world load. A helper success file is never used as authority to skip that validation. If the owning Steam session is unavailable, setup waits and no island is loaded.

## Hosting and lifecycle validation still required

Before moving a live island, use a copied save in a new test data volume and verify all of the following on the actual Linux host: real Steam validation, complete bootstrap/load, sustained simulation beyond the old failure window, matching client replay/checkpoints, saves, reconnect, and graceful stop/restart. Compare the copied world's population and entities before and after. Authentication success alone does not prove Wine's long-session compatibility.

When these checks pass, `COI_AUTH_ONLY=false` starts the Windows host under the same Wine prefix and user as Steam. It resumes `/data/island.save` if present, otherwise uses `COI_MAP` and `COI_STARTING_FACTORY`. The default autosave interval is 60 seconds, controlled by `COI_AUTOSAVE_SECONDS`.

Container-console controls are:

```sh
/app/control.sh --command status
/app/control.sh --command pause
/app/control.sh --command resume
/app/control.sh --command save
/app/control.sh --command shutdown
```

The wrapper reads the control key privately from `/data/control.key`. On container stop, the supervisor requests authenticated shutdown, waits for the simulation to exit, and only then terminates its Wine session. During bootstrap it waits for the control endpoint to become available. The stack grants a 300-second stop period; a shutdown timeout is explicitly an unconfirmed final save, not a successful save. Always retain an independent known-good backup.

Give players only their matching Windows multiplayer client, the Linux host's LAN/VPN address and TCP player port, and the private `join.key`. Never send the Steam profile or `control.key`. The current alpha.6 candidate requires matching alpha.6 clients and revision 6; alpha.5 / alpha.5.1 clients cannot join it. Exact game assembly fingerprints must also match. The [coverage ledger](multiplayer-coverage.md) and [synchronization limits](synchronization.md) still apply.

## Evidence so far

- Official Windows Steam installer downloaded and its Valve Authenticode signature and SHA-256 verified.
- Isolated Ubuntu/Wine 11 image built locally; no live server or installed client changed.
- Windows self-contained .NET 10 host runs after avoiding Docker Desktop's Windows executable bind-mount mapping problem.
- Both .NET 8 and .NET 10 no-world probes reached genuine Steam initialization and correctly refused startup before Steam installation/login. Production remains .NET 10.
- Actual Windows Steam installed and completed its initial self-update in the isolated prefix; the password-protected loopback desktop is available for direct sign-in.
- The owner completed Steam Guard through the genuine Steam mobile QR flow. The installed game's unchanged Steam initializer, valid-client check, and subscription check passed inside Wine. The private profile retained sign-in across Steam relaunches without another Guard prompt.
- A complete, unchanged game-file copy passed a zero-step load of a copied pre-incident island: step 114859, population 97, and 87 entities. SHA-256 checks of the principal game assemblies, native Steam library, game executable, and Unity player matched the original installation. Omitting root game files initially triggered native integrity Err #13; supplying the complete files resolved it without changing the check.
- Candidate Wine initialization passed with the actual read-only root, dropped capabilities, no-new-privileges, and non-executable `/tmp` restrictions. It waited for a private VNC password, loaded no island, and stopped with exit code 0.
- Supervisor regressions exercise the actual shutdown function: command ACK followed by host exit 0 succeeds; ACK followed by a failed final save/host exit 1 remains failure; stopping before a world exists succeeds. An ACK alone is not treated as proof of a completed save.
- **Current blocker:** Steam exits with code 1 after roughly 40–53 seconds in local tests. When its session is missing, the corrected server refuses world loading. Blank Steam UI text is also unresolved; the genuine mobile QR flow worked for sign-in. No client/game validation was disabled.
- **Pending:** stable Steam lifetime, sustained island simulation, two-client replay, save/shutdown/restart, exact release-download-stage build, and target Portainer validation. The current files are a candidate, not a ready-to-deploy release.
