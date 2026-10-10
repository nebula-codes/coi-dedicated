# Join a dedicated island

This **alpha** connects Captain of Industry to this project's dedicated server. It supports a limited set of shared actions; it is not complete co-op gameplay or a CoopFoundry client.

## What you need

- Windows x64 and your own installed copy of **Captain of Industry 0.8.7d**.
- The **client ZIP from the same release** as the server. The game assemblies must match exactly. Keep the host and players on the same DLC configuration; other mods and DLC combinations have not been validated.
- The host's address, player port (normally **28551**), and a private copy of their **join.key** file.

Download [CoI-Multiplayer-Client-0.1.0-alpha.1-win-x64.zip](https://github.com/nebula-codes/coi-dedicated/releases/download/v0.1.0-alpha.1/CoI-Multiplayer-Client-0.1.0-alpha.1-win-x64.zip). You do not need the source archive, a .NET SDK, or the server-manager package to play.

## Install

1. Close Captain of Industry completely.
2. Extract the entire client ZIP to a normal folder. Do not run the installer from inside the ZIP.
3. Run **Install Client.cmd**. It finds Steam installations or asks for the game directory. Select the directory containing the game's executable and `Captain of Industry_Data`.
4. Start the game normally through Steam.

The installer verifies its bundled files and installs BepInEx plus the dedicated plugin. It does not overwrite original game assemblies. If it reports an existing-loader conflict, leave those files in place and resolve the conflict before continuing; forcing an overwrite can break other mods. Updates back up replaced dedicated-plugin files.

## Connect

1. Save the host's `join.key` somewhere private on your PC.
2. At the game's **main menu**, press **F8**.
3. Enter the server address, player port **28551** (or the host's custom port), and the full local path to `join.key`.
4. Choose **Join live dedicated island** and wait for the island to load.

Use the host computer's LAN address on the same network, or its VPN address for remote play. Use `127.0.0.1` only when the server is running on your own PC. The key field takes a **file path**, not the key's contents.

The server pauses briefly while a joining player loads a verified snapshot. It continues running after everyone disconnects. The F8 snapshot-preview option is separate from live play and is not needed to join.

## What works in this alpha

Supported actions include pause, speed 1, ordinary machine and standard-storage placement, construction priority, machine recipe toggles, ordinary deconstruction, and construction toggles. The game's normal research, placement, and construction-cost rules still apply.

Roads, belts, pipes, other transports, terrain editing, batch placement, many specialized buildings, and broader management actions are **not supported**. Unsupported inputs are rejected. Long sessions, large islands, and complete cross-runtime synchronization remain unproven. Use a separate test island and keep backups.

If the connection drops or the client detects a tick mismatch, its island freezes. Return to the main menu and join again. Automatic state repair and resynchronization are not implemented.

## Troubleshooting and removal

| Symptom | Check |
| --- | --- |
| F8 panel is missing | Confirm the game was closed during installation and that you selected the correct Steam installation. Look for `BepInEx/LogOutput.log` in the game folder. |
| Connection fails | Check the host address, player port, host running status, and Windows Firewall. Another computer cannot reach the host through `127.0.0.1`. |
| Authentication fails | Ask the host for the current `join.key`; use its local file path. Do not use `control.key`. |
| Compatibility mismatch | Match the server's exact game build and client release. A Steam update may change the game assemblies even if the displayed version looks similar. |
| An action is rejected | It may be unsupported, research-locked, or invalid under the normal game rules. See [command coverage](https://github.com/nebula-codes/coi-dedicated/blob/v0.1.0-alpha.1/src/CoiDedicated.GameAdapter/README.md). |
| Island freezes after joining | Return to the main menu and reconnect. Give the host the error and relevant log excerpt. |

Close the game and run **Uninstall Client.cmd** to remove this dedicated plugin. The uninstaller preserves shared BepInEx files, other plugins, and backups.

Keep `join.key` private to your group. Never post keys or saves in a public issue. This alpha's network traffic is not encrypted; use a trusted LAN or a VPN.
