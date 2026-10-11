# Experimental authenticated Docker / Portainer hosting

**Deployment of the published alpha.5 Docker image remains paused.** Its missing Steam startup validation can cause a delayed pirate game-over and damage an island. Preserve its data volume and separate backups. Do not resume that image. The corrected native Linux host refuses to load an island because it cannot run the installed Windows Steam wrapper. See the [startup incident and validation ledger](server-startup-validation.md).

The **published experimental replacement** is `ghcr.io/nebula-codes/coi-dedicated:0.1.0-alpha.6`, using the Windows server and genuine Windows Steam client together under Wine. **Authentication-only mode is the default: no island loads.** Local tests passed genuine authentication, island startup stress, and native peer replay using Steam's normal Offline Mode. An online session using the playing PC's account was disconnected with `Logged In Elsewhere`. The target NAS/Portainer machine has not been tested; use a copied island for validation before moving live data.

## Why this needs a real Steam client

The server calls the game's unchanged Facepunch `SteamClient.Init`, checks `SteamClient.IsValid` and `SteamApps.IsSubscribed`, and stops before world loading on failure. Valve documents that client initialization requires Steam running in the same user context with an account owning the application. [Steamworks initialization](https://partner.steamgames.com/doc/sdk/api), [subscription API](https://partner.steamgames.com/doc/api/ISteamApps).

A generic anonymous game-server login or a connecting player's authentication ticket does not establish this local owning-client session. These APIs have different purposes; this project cannot substitute them for CoI's checks. [Steam game-server API](https://partner.steamgames.com/doc/api/ISteamGameServer), [authentication tickets](https://partner.steamgames.com/doc/features/auth). No application-ID overrides, fabricated subscription responses, borrowed credentials, or game patches are used. Session information stays in the owner's private Steam profile, never the public image, stack configuration, or release files.

For simultaneous **online Steam sessions**, plan for a **separate hosting Steam account with a valid CoI subscription** when playing on another PC. Valve documents the account conflict between a standalone dedicated server and a game client using the same account. This prototype uses CoI's game-client ownership API; it has no publisher-provided anonymous server application. A second account without access to CoI will fail the subscription check. The exact remote hosting configuration still needs runtime testing. [Steam account use](https://help.steampowered.com/en/faqs/view/71EA-CDCE-FB5C-82B3).

Steam also provides a normal Offline Mode using locally retained login and game information. Selecting it through Steam's own UI passed the unchanged ownership checks, island startup stress test, and native peer replay in the isolated Wine environment. Full container lifecycle and target-host verification are recorded below. This project does not simulate Offline Mode by modifying credentials, patching checks, or blocking network traffic. Follow Steam's documented initial online setup; if Steam requires login again, complete it normally. [Steam Offline Mode](https://help.steampowered.com/en/faqs/view/0E18-319B-E34B-B2C8).

## Image layout

The image targets Linux **amd64** Docker Standalone, with Windows x64 .NET self-contained binaries and Wine development release 11.19. Ubuntu's base image digest and Wine package version are pinned in the Dockerfile. Wine 11.0 crashed in device-notification cleanup after Steam's session disconnect; 11.19 handled the same disconnect with a clean exit, but does not resolve the account conflict. The official Steam installer is downloaded into the private profile and checked against a pinned SHA-256; Steam subsequently updates itself normally. A changed installer checksum stops setup until the new official installer is reviewed.

- `/game`: your complete matching Windows CoI 0.8.7d installation, mounted read-only from the Linux host. Game files are never put in the public image or source context.
- `/data`: island, saves, control key, and player join key. Use a separate new volume for candidate testing.
- `/profile`: private Wine prefix, Steam installation and login state, VNC password, and private authentication diagnostics. Keep it separate from shareable island backups. Do not publish, export to friends, or upload this volume.
- TCP 28551: the player endpoint, inactive in authentication-only mode. Keep it on a trusted LAN/VPN.
- TCP 28550: administrative endpoint inside the container only.
- TCP 6080: Steam desktop web interface, published **only on the host's loopback** at port 28680. No server-manager web dashboard is included.

The application runs as UID/GID **1654**, with a read-only root filesystem, dropped capabilities, no privileged mode, and no Docker socket mount. Allow several gigabytes for the Wine image, prefix, and Steam updates, in addition to your game files and island data. The exact long-session resource requirements remain unmeasured.

Docker Desktop on Windows is only a local proof environment. Its Windows bind mounts caused Wine to fail when mapping Windows executable/assembly images. The proof used unchanged files copied into a private native Docker volume; this is not evidence that the game files or .NET needed modification. The deployment target is a Linux host with native filesystem mounts.

## Published image and optional source build

The image was published by the successful [container Actions run](https://github.com/nebula-codes/coi-dedicated/actions/runs/38099804323), and an anonymous public pull was verified. Its published digest is `sha256:e406c61b3ef34d31cda0ef7e5c4f3a3b64c42bc9e37ccf42c170a1d626dc2a6a`.

[compose.portainer.yml](../compose.portainer.yml) still requires explicit `COI_IMAGE` selection. Use the published tag above, or pin the exact image in Portainer's stack environment:

```text
COI_IMAGE=ghcr.io/nebula-codes/coi-dedicated@sha256:e406c61b3ef34d31cda0ef7e5c4f3a3b64c42bc9e37ccf42c170a1d626dc2a6a
```

There is deliberately no automatic fallback to the broken alpha.5 image or ZIP. For an optional source build, [compose.portainer.build.yml](../compose.portainer.build.yml) additionally requires:

- `COI_BUILD_CONTEXT`: a reviewed source checkout or Git URL containing this Dockerfile.
- `COI_SERVER_VERSION`, `COI_SERVER_ARCHIVE_URL`, `COI_SERVER_ARCHIVE_SHA256`: a corrected Windows manager archive and its checksum.

That archive must contain `bin/server`, `bin/control`, and `bin/steam-check`. Older manager archives without the new helper are rejected. The build extracts only these redistributable applications, and checks that game assemblies, keys, and saves are absent. It never sends `/game` or the persistent profile to the build context. The corrected [alpha.6 Windows release](https://github.com/nebula-codes/coi-dedicated/releases/tag/v0.1.0-alpha.6) supplies these inputs. Publication and local tests do not establish compatibility with the target NAS or every Steam setup.

The published Windows manager archive has SHA-256 `9b8fb250fdfe27008e76a6045a477220a4ba1f02a9962e78f2ae649cb786f1d1`. Use its release download URL and `COI_SERVER_VERSION=0.1.0-alpha.6` for a candidate build. Do not use the older Linux archive.

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

The first setup deliberately waits before calling the Steam SDK: activating the game too early can disconnect an online session used by the playing PC. Finish Steam's own setup first. For the tested offline configuration, select **Steam → Go Offline…** and complete any Steam restart; its menu should then offer **Go Online…**. The Steam menu labels rendered in the local test even when other client text was blank. Then run in the container console:

```sh
/app/steam-ui.sh validate
```

This releases a persistent setup gate; it does **not** declare ownership or bypass validation. Genuine checks begin afterward and run again before every world load. Keeping `COI_AUTH_ONLY=true` still prevents an island from loading. Normal restarts retain this setup choice, while Steam remains responsible for the validity of its cached session.

Steam's Offline Mode concerns its Steam session; this mod's TCP player endpoint remains available. A native Windows peer connected through Docker's published player port and replayed 120 updates with a verified checkpoint while Steam in the container remained offline.

The no-world `CoiDedicated.SteamCheck` helper performs the same real startup checks used by the server. The public container log reports success or waiting status; native authentication diagnostics stay in `/profile/logs/steam-check.log` because SDK output may include account identifiers. In authentication-only mode, a healthy status means the latest check passed, **not** that gameplay or save handling has been validated.

The host repeats the checks itself before each world load. A helper success file is never used as authority to skip that validation. If the owning Steam session is unavailable, setup waits and no island is loaded.

To reauthenticate or change Steam's mode, first stop any world and run the container with `COI_AUTH_ONLY=true`. Run `/app/steam-ui.sh pause-validation`, then **restart the container before changing Steam sign-in or mode**, so any in-flight check finishes. Complete Steam setup in its UI and run `validate` again. Restart also reopens Steam if it exited. The supervisor does not automatically relaunch Steam over a running island. Do not repeatedly retry an online session while the same account is being used on the playing PC.

## Hosting and target-machine validation

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

The wrapper reads the control key privately from `/data/control.key`. On container stop, the supervisor requests authenticated shutdown and waits for the simulation to exit. It then asks Steam to shut down normally so Steam can flush its private sign-in/offline state before Wine ends. This same cleanup runs when the host exits after a control shutdown. During bootstrap it waits for the control endpoint to become available. The stack grants a 300-second stop period; an island shutdown timeout is explicitly an unconfirmed final save, not a successful save. A Steam shutdown timeout also returns failure and warns that reauthentication may be needed. Always retain an independent known-good backup.

Give players only their matching Windows multiplayer client, the Linux host's LAN/VPN address and TCP player port, and the private `join.key`. Never send the Steam profile or `control.key`. The current alpha.6 candidate requires matching alpha.6 clients and revision 6; alpha.5 / alpha.5.1 clients cannot join it. Exact game assembly fingerprints must also match. The [coverage ledger](multiplayer-coverage.md) and [synchronization limits](synchronization.md) still apply.

## Evidence so far

- The experimental alpha.6 image is published at the tag and digest above; its Actions build succeeded and anonymous public pull was verified.
- Official Windows Steam installer downloaded and its Valve Authenticode signature and SHA-256 verified.
- Isolated Ubuntu/Wine 11 image built locally; no live server or installed client changed.
- Windows self-contained .NET 10 host runs after avoiding Docker Desktop's Windows executable bind-mount mapping problem.
- Both .NET 8 and .NET 10 no-world probes reached genuine Steam initialization and correctly refused startup before Steam installation/login. Production remains .NET 10.
- Actual Windows Steam installed and completed its initial self-update in the isolated prefix; the password-protected loopback desktop is available for direct sign-in.
- The owner completed Steam Guard through the genuine Steam mobile QR flow. The installed game's unchanged Steam initializer, valid-client check, and subscription check passed inside Wine. The private profile retained sign-in across Steam relaunches without another Guard prompt.
- A complete, unchanged game-file copy passed a zero-step load of a copied pre-incident island: step 114859, population 97, and 87 entities. SHA-256 checks of the principal game assemblies, native Steam library, game executable, and Unity player matched the original installation. Omitting root game files initially triggered native integrity Err #13; supplying the complete files resolved it without changing the check.
- Candidate Wine initialization passed with the actual read-only root, dropped capabilities, no-new-privileges, and non-executable `/tmp` restrictions. It waited for a private VNC password, loaded no island, and stopped with exit code 0.
- Supervisor regressions exercise the actual shutdown function: command ACK followed by host exit 0 succeeds; ACK followed by a failed final save/host exit 1 remains failure; stopping before a world exists succeeds; hung Steam cleanup returns failure. The fixture uses the lowercase `steam.exe` produced by Steam's updater. An ACK alone is not treated as proof of a completed save.
- The Dockerfile's actual download stage fetched the published final alpha.6 Windows archive, verified its SHA-256, extracted all three required applications, and checked that the payload contains no game assemblies, keys, or saves.
- Wine 11.19 passed the unchanged no-world Steam validation with the final published alpha.6 helper. Mono/Gecko suppression is scoped to prefix initialization; runtime leaves the `mscoree` loader enabled so CoreCLR can load managed assemblies.
- An online session reported `Logged In Elsewhere` immediately after SDK initialization. Wine 11.0 then crashed in device-notification cleanup; Wine 11.19 handled that disconnect with a clean exit and no new crash dump. The newer Wine does not make concurrent online use of one Steam account work.
- Selecting Steam's normal **Go Offline** menu option passed the unchanged final alpha.6 helper and host checks. A 3,100-update copied-island run completed and saved normally. The stronger unchanged startup regression placed 120 native storage plans across entity IDs 100 and 200, then ran 3,100 more updates: population stayed 97, all 16 original vehicles remained free of forced scrap scheduling, and no pirate callbacks, pirate logs, or game-over occurred.
- Two native simulation peers inside the isolated Wine container each completed 400 updates with matching checkpoints and final raw serialized world hashes. Pause/resume, speeds 2/3/4, and machine placement replicated. A fresh same-name reconnect completed 120 more updates and verified two checkpoints. These are native simulation tests, not a Unity overlay or remote-LAN play test.
- The full production image built from the public alpha.6 Windows ZIP. Its exact read-only root, capability restrictions, and non-executable `/tmp` passed fresh prefix initialization and .NET apphost loading. The explicit setup gate kept SDK checks and world loading stopped until requested; auth-only health correctly remained unready.
- Full production-entrypoint authentication-only setup passed, then hosting started from the saved island with the retained genuine offline Steam profile. A Windows-native peer connected through the published Docker port, replayed 120 updates, and verified its checkpoint with the server.
- Explicit pause/save/status followed by Docker stop completed the final save and normal Steam shutdown with container exit 0. Restart validated Steam again and loaded the exact saved step 130545, preserving population 98 and 88 entities. The server then resumed simulation normally; this does not claim paused state persists across startup. A subsequent control-command shutdown also saved, shut Steam down normally, and exited 0. Test services were stopped and their private volumes retained.
- **Still unverified:** deployment on the actual remote Linux/Portainer machine, multi-hour stability, and human Unity-client gameplay against this container. The full production lifecycle used a retained genuine Steam profile; the official Steam installer succeeded in the earlier isolated proof, but a completely fresh Steam installation/sign-in under all final image restrictions has not been exercised. The final fresh-profile test covered prefix initialization and the password gate. Blank Steam UI text also remains partly unresolved; Steam's menu and genuine mobile QR flow were usable. No client/game validation was disabled.
