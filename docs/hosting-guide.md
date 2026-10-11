# Host a dedicated island

The Windows x64 server runs the island without an open Unity game window or a connected player. **Alpha.6.1 does not require Steam running or a Steam login on the server.** It still requires your own supported Captain of Industry game files. The local browser manager controls its process, settings, saves, and backups. This **alpha** still has limited multiplayer action coverage; read the [player guide](https://github.com/nebula-codes/coi-dedicated/blob/codex/gameplay-and-presence/docs/player-guide.md) before starting a shared island.

**Do not host with the published alpha.5 server ZIP or Docker image.** A startup defect in those assets omits the game's Steam validation and can trigger a delayed pirate game-over, including population loss and vehicle scrapping. The startup correction is in current source; those published assets remain affected. Preserve the current island and any pre-incident backups separately. Correcting startup does not undo damage already saved to an island. See [startup validation and test status](server-startup-validation.md).

## First start

1. Provide your own **Captain of Industry 0.8.7d** game files on a Windows x64 host. Keep the complete installation, including `Captain of Industry_Data` and `Maps`.
2. Extract the **alpha.6.3 Windows server-manager ZIP**, or build current source with the **.NET 10 SDK**. The ZIP includes its runtime and requires no SDK. The old published alpha.5 server assets are affected and must not be used.
3. With all profiles stopped, run **Start Server Manager.cmd** from the extracted package. For source builds, rebuild using `.\scripts\build.ps1`, then run **Server Manager.cmd**. Reopening an old dashboard does not replace its host binary.
4. Open the dashboard at **http://127.0.0.1:28560** if the browser does not open automatically.
5. In **Settings**, confirm the installed game directory, map, ports, and autosave interval. Choose a name for the island and save the settings.
6. Click **Start** and wait for **Running**. The server checks the exact supported game binaries before creating or loading an island, then prepares the world before accepting players.

The launcher looks for Steam installations as a convenience. If discovery fails, set the game directory in the dashboard; Steam does not need to be installed or running on the server. Each PC running the game client needs matching game assemblies and DLC configuration. Alpha.6.3 retains compatibility revision 6, but every player should install the alpha.6.3 client to tolerate the full 600-second join window. Older alpha.6 clients retain shorter download and no-tick timeouts. Alpha.5 clients cannot join. The current validation used the base game; other mods and DLC combinations remain unverified.

Startup recognizes the exact SHA-256 fingerprints of the supported `Mafi.dll`, `Mafi.Core.dll`, and `Mafi.Base.dll` from CoI **0.8.7d**, including the assemblies actually loaded into the host. It applies a server-only startup compatibility setting in memory; it does not modify those game files or initialize Steam. Unknown or changed binaries stop startup before world preparation. An updated game version needs a reviewed compatibility update for this host; changing a displayed version string is insufficient. Check the server log for the unsupported filename and fingerprint, and keep the matching game version for both server and players. See [startup validation](server-startup-validation.md) and the [Docker / Portainer guide](docker-portainer.md).

The populated **New Haven** profile is the default. Map and starting-factory settings affect a new island only. An existing `island.save` is resumed when the server starts.

## Let friends connect

For players on other computers, stop the profile, set its listen address to your LAN/VPN interface address or **0.0.0.0**, and start it again. Allow incoming **TCP 28551** through Windows Firewall for the network you use. The manager does not change firewall or router settings.

Share the host's LAN/VPN address, player port, this release's client download, and the profile's **join.key** download with your players privately. Follow the [player guide](https://github.com/nebula-codes/coi-dedicated/blob/codex/gameplay-and-presence/docs/player-guide.md) to install the client and join through F8.

| Default port | Purpose | Exposure |
| --- | --- | --- |
| TCP 28551 | Game clients | Allow to your players over a trusted LAN or VPN. |
| TCP 28550 | Server administration | Keep private; do not forward it on your router. |
| TCP 28560 | Browser manager | Loopback only; do not forward it on your router. |

The server's gameplay and admin endpoints use the profile's listen address. Firewall rules should expose only the player port. **control.key stays with the administrator**; players receive only `join.key`. Network traffic is not encrypted, so use a trusted LAN or a VPN for remote play.

## Daily controls

- **Pause / Resume** changes the shared simulation state. The initial server runs at speed 1; clients can select shared speeds 1–4.
- **Save** writes the current island. Autosaves run at the configured interval.
- **Stop** requests a final save and a graceful process exit. During startup, the manager waits for the server to finish initialization before requesting shutdown.
- **Restart** saves and stops the process, then starts the same island again.
- **Console** shows recent output and offers a log download. The overview shows authenticated status, simulation step, player count, and resource information.

Closing the browser or manager leaves the server running. Relaunching the manager from the **same extracted folder and data directory** reconnects to its recorded process. Stop all profiles before moving or replacing that folder. Windows reboot startup, automatic crash restart, and Windows service installation are not included.

If a server becomes unresponsive, the manager reports it and keeps its process intact. It does not force-kill a potentially unsaved island.

## Saves and backups

The portable release keeps profiles and their state under **data/default** beside the launcher. Each profile has its own data directory, keys, logs, and backup library. Source-checkout launchers use `manager-data` instead. Preserve the entire data directory when upgrading; do not publish or share it as part of the software.

Use **Saves & backups → Create backup** for a consistent snapshot, including while the server is running. Backup downloads are useful for a separate offline copy. Autosaving preserves one previous save generation; it does not replace a separate backup library.

To load a different island:

1. Stop the profile and wait for **Stopped**.
2. Import a `.save` file into **Saves & backups**, or select an existing backup.
3. Choose **Restore**, then start the profile.

Importing adds a library entry; it does not immediately replace the active island. Imports accept files up to **256 MiB** and check the CoI header. The game checks compatibility when loading. Restoring verifies the recorded SHA-256, archives the current island first, and atomically replaces the active save.

Use a separate test island for this release. Full gameplay coverage, long-running stability, and complete state checksums remain unfinished. Selected-state divergence checks and bounded client snapshot recovery are implemented; actual Unity recovery and expanded overlay visuals still need an in-game check.

## Updating and reporting problems

Stop all profiles, take an offline backup of the data directory, and keep the previous release before replacing program files. Closing the browser leaves the manager running; after all profiles show **Stopped**, end that package's `CoiDedicated.Manager.exe` process in Windows Task Manager before replacing its binaries. Update the server and all players to alpha.6.3 for ten-minute joins; preserve client settings and join keys. Earlier alpha.5 clients still require updating. Game assembly fingerprints are checked at startup and connection time; a game update requires a supported host update and matching client game files.

For an [issue report](https://github.com/nebula-codes/coi-dedicated/issues), include the release tag, CoI version, Windows version, steps to reproduce, and a short relevant log excerpt. Remove personal paths and addresses as appropriate. **Never attach `join.key`, `control.key`, or an entire data directory.** Review a save before sharing it with anyone.
