# Captain of Industry dedicated server manager

This experimental Windows x64 package includes the server, browser dashboard, and .NET runtime. It requires your own installed copy of Captain of Industry **0.8.7d**. The game itself is not included.

1. Extract the complete ZIP into a writable folder. Keep `bin` beside the launcher files.
2. Double-click **Start Server Manager.cmd**. The launcher checks your Steam libraries and opens `http://127.0.0.1:28560`.
3. Check the game installation folder under **Settings**. If Steam discovery did not find it, enter the folder containing `Captain of Industry_Data` and `Maps`.
4. Start the island from the dashboard. First startup builds and prepares its save before the server becomes ready.

No SDK, runtime installation, administrator access, running Steam client, Steam login, or running game window is required on the server. Supply your own supported game installation. Alpha.6.1 checks the exact CoI 0.8.7d simulation DLL fingerprints and stops before loading an island if they differ. It applies startup compatibility in memory and leaves the game files unchanged. Use the alpha.6.3 client for every player to tolerate the full 600-second join window. Older alpha.6 clients retain shorter download and no-tick timeouts; every player still needs the same game version.

For LAN play, set the server listen address to `0.0.0.0`. Players use the server computer's LAN address, the player port (default **28551**), and the player join key downloaded from the dashboard. Install the matching client package on each player's computer. The admin control port is **28550** by default; the browser dashboard stays local to the server computer. Allow the player port through your firewall when needed. Share the player `join.key` only with invited players; keep `control.key` private.

The default profile settings, saves, backups, and runtime metadata live under `data\default`. Keep that folder when upgrading. Stop all islands in the dashboard and close the manager before replacing package binaries. Closing a browser tab or the manager does not stop an island; reopening the same manager reconnects to its recorded server process. Use **Stop** to save and shut down an island cleanly.

Optional PowerShell commands from the extracted folder:

```powershell
.\'Start Server Manager.ps1' -GameDir 'D:\SteamLibrary\steamapps\common\Captain of Industry'
.\'Start Server Manager.ps1' -Port 28560 -DataDir 'D:\CoI Server Data'
.\'Start Server Manager.ps1' -Check
```

`-GameDir` supplies the default installation for new profiles. Existing profiles retain their Settings. `-Check` checks the package and game discovery without launching anything. To run another manager, choose a different dashboard port and a separate data directory, then configure distinct player and admin ports.

Manager launch logs are written to `data\default\manager-*.log`. Each profile's server log appears in the dashboard and under `data\default\runtime\<profile-id>\server.log`. If the installation path or game version is wrong, correct it in Settings and restart the island. The dashboard can open even when the game has not been found.

This remains a prototype with a limited multiplayer command set. Unsupported actions are blocked; use copies of valuable saves and keep backups.
