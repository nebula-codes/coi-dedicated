# Join a dedicated island

This **alpha** connects Captain of Industry to this project's dedicated server. It supports a limited set of shared actions; it is not complete co-op gameplay or a CoopFoundry client.

## What you need

- Windows x64 and your own installed copy of **Captain of Industry 0.8.7d**.
- The **alpha.6.3 client and server** for the full ten-minute join window. Older alpha.6 clients still use protocol revision 6, but retain shorter download and no-tick timeouts; update every player, including players already on the island. The game assemblies must match exactly. Keep the host and players on the same DLC configuration; other mods and DLC combinations have not been validated.
- The host's address, player port (normally **28551**), and a private copy of their **join.key** file.

Download the matching Windows client ZIP from [GitHub Releases](https://github.com/nebula-codes/coi-dedicated/releases). You do not need the source archive, a .NET SDK, or the server-manager package to play.

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

The server pauses while a joining player downloads and loads a verified snapshot. Alpha.6.3 allows **600 seconds (ten minutes)** after authentication for that player to become ready; a stalled join is disconnected when that window expires. Players already connected wait through the loading pause. It continues running after everyone disconnects. The F8 snapshot-preview option is separate from live play and is not needed to join.

Joining remembers the server address, port, player name, and full join-key path across game and PC restarts. Keep the key file at that location. Only its path is saved in `BepInEx/config/local.coi.dedicated.client.cfg`; key contents are never copied into that configuration. A paused control snapshot does not replace the remembered player key.

## What works in this alpha

Alpha.6 adds all 87 previously missing UI-observed command classes, including advanced train, industrial/world, cut/paste, surface and space controls. Alpha.6.3 retains those commands and protocol revision 6. Specialized copied settings and some native variants still have limits. See [the full coverage ledger](multiplayer-coverage.md) for tested paths and remaining restrictions.

Set a distinct player name in **F8 before joining** so others recognize you. **Show and share player cameras and building footprint previews** controls the indicators. Colored labels mark other players' camera focus, including off-screen directions; rectangles approximate their views. Outlines show active building, transport, rail, paving, and terrain-tool previews. They are advisory outlines, not construction commands or textured ghosts. Visual placement still needs broader two-player in-game checks.

The inspector's add/remove truck controls include assigning trucks to a tree harvester. Normal, Shift/Ctrl batch, and Alt zone selection use the game's native assignment commands.

Shared speeds **1×–4×** are supported; higher speeds are rejected. Some specialized placement settings and detail controls remain unsupported and explain their rejection. Protocol revision 6 cannot join alpha.5 or older servers. Movement interpolation remains enabled in the current client.

If the connection drops or a simulation/state check fails, the client freezes its island and attempts a fresh server snapshot. Automatic reconnect is enabled by default and makes at most three attempts, with 2, 5, and 15 second retry delays. A brief successful join does not reset that limit; one healthy minute does. F8 lets you disable recovery or cancel a pending retry. Manual disconnect does not reconnect automatically. Unity scene transitions still require an in-game check; if recovery fails, return to the main menu and join manually.

State checkpoints compare selected gameplay fields. They detect several kinds of divergence, but do not prove every private game field is identical. These are still experimental sessions; see [synchronization details](https://github.com/nebula-codes/coi-dedicated/blob/main/docs/synchronization.md).

## Troubleshooting and removal

| Symptom | Check |
| --- | --- |
| F8 panel is missing | Confirm the game was closed during installation and that you selected the correct Steam installation. Look for `BepInEx/LogOutput.log` in the game folder. |
| Connection fails | Check the host address, player port, host running status, and Windows Firewall. Another computer cannot reach the host through `127.0.0.1`. |
| Authentication fails | Ask the host for the current `join.key`; use its local file path. Do not use `control.key`. |
| Compatibility mismatch | Match the server's exact game build and client release. A Steam update may change the game assemblies even if the displayed version looks similar. |
| An action is rejected | It may be unsupported, research-locked, or invalid under the normal game rules. See [command coverage](https://github.com/nebula-codes/coi-dedicated/blob/main/docs/multiplayer-coverage.md). |
| Island freezes after joining | Let automatic reconnect finish, or use F8 to cancel and reconnect manually from the main menu. Give the host the error and relevant log excerpt if it repeats. |
| Joining times out, or existing players disconnect while someone loads | Update the server and every player to alpha.6.3. Older clients retain shorter limits even when the server allows ten minutes. A join that exceeds ten minutes still times out. |

Close the game and run **Uninstall Client.cmd** to remove this dedicated plugin. The uninstaller preserves shared BepInEx files, other plugins, and backups.

Keep `join.key` private to your group. Never post keys or saves in a public issue. This alpha's network traffic is not encrypted; use a trusted LAN or a VPN.
