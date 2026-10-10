# Presence geometry checks

Run against the supported local CoI installation:

```powershell
dotnet run --project tests/CoiDedicated.PresenceGeometry.Tests -c Release --artifacts-path .work/presence-geometry-tests
```

The checks cover edge/behind-camera marker placement, viewport line clipping, surface designation bitmap selection, bounded sampling and wire round trips for long transport paths, and the exact installed preview-field types. They compile the actual client helper sources. No game, live server, simulation, rendering, or input commands are started.

The collectors read only existing active preview state. Transport waypoint access is guarded by the existing geometry cache to avoid triggering lazy preview generation. Rail outlines use each plan step's transformed layout bounds. Terrain outlines use the designation's actual four corner heights. Surface paving outlines include only selected tiles and sample current terrain heights. The previous layout preview collector also retains active building and road-layout footprints. Removal tools, rail junction-specific overlays, and copied transport/rail graphs outside these active tools are not separately shared.

The installed Unity artifact used for field discovery was `Mafi.Unity.dll` 0.8.7.0, SHA-256 `f0dc98b43e1e88a429c6eb494e40264be08bc0a3aa7b9bbf1fe50b3a20082a5f`, REA Evidence `ev_842a47fe3cbbd4a1e13b423370e4361621e217dd6079bd05d564a3726ba3097b`. REA's inventory has partial custom-attribute coverage; focused local ILSpy reads established the preview fields and lifecycle, not runtime behavior. Decompiled game code stays in ignored `.work/presence-tools`.

Unity's final appearance and controller selection must still be checked in-game with two clients. These tests do not claim visual validation, exact 3D ghosts, or proof that a preview's construction command is supported. Planning outlines disappear when the game clears or deactivates its preview, and presence never commits construction.
