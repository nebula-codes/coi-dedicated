# Captain of Industry dedicated server prototype

An independent Windows server that runs Captain of Industry's island simulation using the owner's installed game assemblies. It runs without Unity, a graphics device, or a player hosting a game window. It saves the island, resumes it on restart, and accepts authenticated clients through a matching experimental game plugin.

**Development prototype for CoI 0.8.7d.** This is a custom server/client pair, not a CoopFoundry server. Multiplayer command coverage and runtime validation are still being expanded. Game updates, other mods, and DLC combinations require compatibility testing. The repository contains integration code, not redistributed game binaries.

## Download the alpha

Get the ready-to-run Windows packages from [GitHub Releases](https://github.com/nebula-codes/coi-dedicated/releases/tag/v0.1.0-alpha.1). Everyone needs their own matching copy of Captain of Industry. The player package requires no SDK; the server-manager package includes its .NET runtime.

| Package | Start here |
| --- | --- |
| `CoI-Multiplayer-Client-0.1.0-alpha.1-win-x64.zip` | Extract, close the game, run **Install Client.cmd**. Follow the [player guide](docs/player-guide.md). |
| `CoI-Server-Manager-0.1.0-alpha.1-win-x64.zip` | Extract and run **Start Server Manager.cmd**. Follow the [hosting guide](docs/hosting-guide.md). |

Use these for experimental sessions: roads, transports, terrain editing, and other commands remain unsupported. Full-state determinism, long sessions, and automatic resynchronization are not proven. Back up islands you care about. Share the player `join.key` privately; never publish keys, your saves, or the game installation.

For release changes and tested behavior, see the [alpha release notes](docs/releases/v0.1.0-alpha.1.md). Source builds are described below. This project's source is MIT-licensed; bundled dependency notices are described in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Server manager from source

In a source checkout, double-click **Server Manager.cmd** to open the local dashboard at `http://127.0.0.1:28560`. It builds missing binaries on first use. The release ZIP instead uses **Start Server Manager.cmd**. Or run from source:

```powershell
.\scripts\start-manager.ps1
```

The dashboard provides independent island profiles, start/stop/restart, pause/resume, save, authenticated live status, player counts, resource statistics, searchable console logs, and player-key downloads. Configure the game directory, shipped map, network binding, ports, and autosave interval in **Settings** while the server is stopped. Map and starting-factory settings only affect a new island; an existing save resumes as-is.

**Backups** captures a consistent snapshot of a running island. Importing a `.save` file adds it to the backup library; restore it while stopped to make it the active island. Imports check the save header and size; the game verifies compatibility on startup. Restoring verifies SHA-256 and archives the current island first. Files live under `manager-data`; each profile has separate save, key, log, and backup directories. CLI `server-data` islands can be imported from the Backups screen.

Closing the browser or restarting the manager leaves managed servers running. Reopening the manager reconnects to its recorded processes. **Stop** requests a final save and waits for the process to exit; during initialization it waits until the admin endpoint is available. Unresponsive servers are reported and never forcibly killed. This version does not install a Windows service or automatically restart crashed servers or servers after a Windows reboot.

The dashboard listens on this computer's loopback address only. Use a LAN bind address in the server profile to expose gameplay to other machines; network/firewall setup and client requirements are described below. Administrative keys remain on the server. The manager controls this project's experimental host; it does not change its multiplayer command coverage.

## Start the server from the command line

Requires Windows, an installed copy of Captain of Industry, and the .NET 10 SDK. This machine's default game directory is `F:\SteamLibrary\steamapps\common\Captain of Industry`; set `COI_GAME_DIR` or supply `-GameDir` for another installation.

From this repository:

```powershell
.\scripts\build.ps1
.\scripts\start-server.ps1
```

The first start creates a populated **New Haven** island under `server-data`. Later starts resume `server-data/island.save`. Initial world preparation runs the game's normal save/load repair pass before accepting players; startup can take a little while. **Ctrl+C** saves and stops. Autosaves retain one previous generation as `island.save.previous`.

For LAN access, bind to the server's LAN address or all interfaces:

```powershell
.\scripts\start-server.ps1 -Listen 0.0.0.0
```

Gameplay uses **TCP 28551**. Administration uses **TCP 28550** and a separate key. The software does not change Windows Firewall or router settings. Use a trusted LAN or a VPN for remote play; this prototype's TCP traffic is not encrypted. Share `server-data/join.key` with players through a trusted channel; keep `control.key` with the administrator.

## Connect a game client

Close the game, then install the built plugin:

```powershell
.\scripts\install-client.ps1
```

Start Captain of Industry normally. At the main menu, press **F8**, enter the server address, port **28551**, and the path to a local copy of `join.key`, then choose **Join live dedicated island**. Each join downloads a verified snapshot into its own save folder. The server briefly holds ticks while the joining client loads, with a 60-second limit, then resumes. It continues running after players disconnect.

The client uses BepInEx 5.4.23.5 and Harmony. Installation records file hashes and backs up replaced files belonging to this plugin under `artifacts`. It stops before writing if an existing third-party loader conflicts. No original game assemblies are overwritten. To disable the client plugin, close the game and move its `BepInEx\plugins\CoiDedicated` directory outside the game directory.

## Admin controls

```powershell
.\scripts\control.ps1 -Command status
.\scripts\control.ps1 -Command pause
.\scripts\control.ps1 -Command resume
.\scripts\control.ps1 -Command save
.\scripts\control.ps1 -Command shutdown
```

Use `-KeyFile` when the server has a custom data directory. Use `-HostName` and `-Port` for remote administration. Never pass the key's contents on the command line.

## Implemented foundation

- Headless creation, simulation, save/load, autosave, exclusive data-directory locking, and clean shutdown.
- Separate player/admin credentials, bounded network messages, snapshot length and SHA-256 validation, command ordering, and tick acknowledgments.
- Server-owned tick order; clients propose allowlisted actions and replay admitted commands. Unsupported actions are blocked locally. A disconnected or out-of-step game client freezes its island.
- Initial command set: pause, normal speed, ordinary machine and storage placement, construction priority, machine recipe enable/disable, ordinary deconstruction, and construction toggles. See [command coverage](src/CoiDedicated.GameAdapter/README.md) for the current list and validation rules.
- Independent headless peer harness for actual game-simulation integration tests. Two peers have replayed shared ticks and commands, with matching selected world/terrain reports; disconnect/rejoin was also checked.
- Two actual Unity client runs completed 100 authoritative ticks. A remote machine placement appeared in the client's simulation, and six input completion/rejection checks passed through its original command objects.
- The final installed payload completed another real-game join and 20 ticks after the storage extension and compatibility revision update.

These checks do not establish complete determinism or long-term stability. Full gameplay actions, large-island soak tests, recovery from state divergence, and unattended service installation remain separate work. The current server handles one island and runs at speed 1. A full-state checksum exchange and automatic resynchronization are not yet implemented.

## Code and checks

| Project | Purpose |
| --- | --- |
| `src/CoiDedicated` | Headless game bootstrap, persistence, game and admin endpoints |
| `src/CoiDedicated.Client` | BepInEx game plugin and synchronized simulation bridge |
| `src/CoiDedicated.Protocol` | Bounded framing, payloads, authentication |
| `src/CoiDedicated.GameAdapter` | Explicit game-command codecs and validation |
| `src/CoiDedicated.Control` | Administration CLI |
| `src/CoiDedicated.Peer` | Independent headless simulation client for tests |
| `src/CoiDedicated.Manager` | Local browser dashboard and HTTP API |
| `src/CoiDedicated.Manager.Core` | Server supervision, profiles, authenticated control, save library |

```powershell
dotnet run --project tests/CoiDedicated.Protocol.Tests -c Release
dotnet run --project tests/CoiDedicated.ControlServer.Tests -c Release
dotnet run --project tests/CoiDedicated.Manager.Tests -c Release
dotnet run --project tests/CoiDedicated.Manager.Storage.Tests -c Release
dotnet run --project src/CoiDedicated.GameAdapter/Tests -c Release
node src/CoiDedicated.Control/Tests/control-smoke.mjs
dotnet build src/CoiDedicated.Client/Tests/SnapshotSmoke.csproj -c Release
node src/CoiDedicated.Client/Tests/snapshot-smoke.mjs
node src/CoiDedicated.Client/Tests/live-smoke.mjs
# Requires a running local test server:
node tests/game-server-wire.mjs .work/live-integration/join.key
# Real manager lifecycle test, isolated ports and data (PowerShell 7):
pwsh -File scripts/test-manager.ps1 -SaveFile server-data/island.save
```

Build release ZIPs locally with your installed game assemblies (they are excluded from the ZIPs):

```powershell
pwsh -File scripts/package-release.ps1 -GameDir 'C:\path\to\Captain of Industry'
pwsh -File scripts/test-client-installer.ps1
```

The release builder writes isolated build outputs, two versioned ZIPs, and `SHA256SUMS.txt` under `artifacts/releases`. The player ZIP includes verified BepInEx files, required notices, and the unmodified Doorstop source archive. The manager ZIP includes its runtime; neither package includes personal server data.

See [world lifecycle](docs/server-lifecycle.md), [client notes](src/CoiDedicated.Client/README.md), [runtime validation and limits](research/runtime-validation.json), and [research evidence](research/coi-dedicated-feasibility.json). Decompiled local evidence, game saves, credentials, downloaded dependencies, logs, and build outputs are excluded from version control.
