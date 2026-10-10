# Host a dedicated island

The Windows server runs the island without an open game window or a connected player. The local browser manager controls its process, settings, saves, and backups. This **alpha** still has limited multiplayer action coverage; read the [player guide](https://github.com/nebula-codes/coi-dedicated/blob/v0.1.0-alpha.1/docs/player-guide.md) before starting a shared island.

## First start

1. Install **Captain of Industry 0.8.7d** on the host PC. The server uses your installed game files; they are not included in the download.
2. Download [CoI-Server-Manager-0.1.0-alpha.1-win-x64.zip](https://github.com/nebula-codes/coi-dedicated/releases/download/v0.1.0-alpha.1/CoI-Server-Manager-0.1.0-alpha.1-win-x64.zip) and extract it completely to a writable folder.
3. Run **Start Server Manager.cmd**. The release includes its .NET runtime; no .NET SDK or separate runtime installation is required.
4. Open the dashboard at **http://127.0.0.1:28560** if the browser does not open automatically.
5. In **Settings**, confirm the installed game directory, map, ports, and autosave interval. Choose a name for the island and save the settings.
6. Click **Start** and wait for **Running**. A first island load prepares the world before accepting players.

The launcher looks for Steam installations. If discovery fails, set the game directory in the dashboard. Each PC running the game client needs the same release's client ZIP, the exact matching game assemblies, and a matching DLC configuration. The current validation used the base game; other mods and DLC combinations remain unverified.

The populated **New Haven** profile is the default. Map and starting-factory settings affect a new island only. An existing `island.save` is resumed when the server starts.

## Let friends connect

For players on other computers, stop the profile, set its listen address to your LAN/VPN interface address or **0.0.0.0**, and start it again. Allow incoming **TCP 28551** through Windows Firewall for the network you use. The manager does not change firewall or router settings.

Share the host's LAN/VPN address, player port, this release's client download, and the profile's **join.key** download with your players privately. Follow the [player guide](https://github.com/nebula-codes/coi-dedicated/blob/v0.1.0-alpha.1/docs/player-guide.md) to install the client and join through F8.

| Default port | Purpose | Exposure |
| --- | --- | --- |
| TCP 28551 | Game clients | Allow to your players over a trusted LAN or VPN. |
| TCP 28550 | Server administration | Keep private; do not forward it on your router. |
| TCP 28560 | Browser manager | Loopback only; do not forward it on your router. |

The server's gameplay and admin endpoints use the profile's listen address. Firewall rules should expose only the player port. **control.key stays with the administrator**; players receive only `join.key`. Network traffic is not encrypted, so use a trusted LAN or a VPN for remote play.

## Daily controls

- **Pause / Resume** changes the shared simulation state. The initial server runs at speed 1.
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

Use a separate test island for this release. Full gameplay coverage, long-running stability, complete state checksums, and automatic resynchronization remain unfinished.

## Updating and reporting problems

Stop all profiles, take an offline backup of the data directory, and keep the previous release before replacing program files. Closing the browser leaves the manager running; after all profiles show **Stopped**, end that package's `CoiDedicated.Manager.exe` process in Windows Task Manager before replacing its binaries. Update server and clients together. Game assembly fingerprints are checked at connection time; a Steam update can make the current pair incompatible.

For an [issue report](https://github.com/nebula-codes/coi-dedicated/issues), include the release tag, CoI version, Windows version, steps to reproduce, and a short relevant log excerpt. Remove personal paths and addresses as appropriate. **Never attach `join.key`, `control.key`, or an entire data directory.** Review a save before sharing it with anyone.
