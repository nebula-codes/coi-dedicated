CAPTAIN OF INDUSTRY - DEDICATED CLIENT

Install on a Windows PC with a licensed copy of Captain of Industry.
This package does not contain the game or require the .NET SDK.

INSTALL
1. Extract the entire ZIP to a folder. Do not run the installer inside the ZIP.
2. Close Captain of Industry.
3. Double-click "Install Client.cmd". Steam installations are detected
   automatically. If necessary, select the folder containing
   "Captain of Industry.exe".
4. Launch the game normally. At its main menu, press F8.
5. Enter the host's server address, game port (usually 28551), and the full
   path to the join.key file the host supplied privately. Join the island.

Use the game version and client release required by the server host. The
dedicated multiplayer project is experimental and does not support every game
action. The host's game version, mods, and DLC configuration must be compatible.
Use a trusted LAN or private VPN. Keep join.key private; it is not included here.

UPDATES AND EXISTING MODS
Run the new release's installer while the game is closed. Matching loader files
are reused. Differing existing BepInEx/Doorstop loader files cause installation
to stop before changing the game. Resolve loader compatibility rather than
deleting another mod's files blindly.

Previous dedicated plugin DLLs are preserved under:
  <game>\BepInEx\CoiDedicatedInstaller\backups\

Package payload checksums are verified before installation. These detect
incomplete or damaged packages; obtain the release from its trusted publisher.
If a checksum fails, download and extract the release again.

REMOVE THE CLIENT
Close the game and run "Uninstall Client.cmd" from this extracted package.
Only the three recorded dedicated plugin DLLs are removed, and only when their
checksums still match the installation record. The shared BepInEx loader,
other mods, settings, and backups remain. The removed DLLs are backed up first.

OPTIONAL COMMAND LINE
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File install-client.ps1 -GameDir "C:\path\to\Captain of Industry" -NonInteractive
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File install-client.ps1 -GameDir "C:\path\to\Captain of Industry" -NonInteractive -Uninstall

Ordinary writable Steam libraries do not need administrator rights. If Windows
denies access to your chosen game folder, check that folder's permissions.
