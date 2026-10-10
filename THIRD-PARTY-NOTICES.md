# Third-party components

Captain of Industry and its game assets are owned by MaFi Games. They are not included in this repository or its downloads. Each player and server operator must supply their own compatible game installation. This is an unofficial project and is not affiliated with MaFi Games or CoopFoundry.

The client release includes the unmodified Windows x64 Mono distribution of BepInEx 5.4.23.5 and these dependencies. Their upstream license texts are retained in `third-party/` in the download:

| Component | Upstream | License |
| --- | --- | --- |
| BepInEx 5.4.23.5 | https://github.com/BepInEx/BepInEx/tree/v5.4.23.5 | MIT |
| HarmonyX / Harmony compatibility libraries | https://github.com/BepInEx/HarmonyX | MIT |
| Harmony | https://github.com/pardeike/Harmony | MIT |
| Mono.Cecil | https://github.com/jbevain/cecil | MIT |
| MonoMod | https://github.com/MonoMod/MonoMod | MIT |
| Unity Doorstop 4.5.0 (`winhttp.dll`) | https://github.com/NeighTools/UnityDoorstop/tree/v4.5.0 | LGPL-2.1 |

The client download also includes the matching Unity Doorstop 4.5.0 source archive under `third-party/`. Doorstop is an unmodified, replaceable dynamic library; its own license applies to that component. Component licenses do not change this project's MIT license.

The server-manager release bundles the Microsoft .NET and ASP.NET Core Windows x64 runtimes. Their MIT licenses and third-party notices are included under `third-party/`. Exact runtime versions are recorded in the published `.runtimeconfig.json` and `.deps.json` files.

Release packages contain no server keys, private configuration, island saves, game assemblies, or game assets.
