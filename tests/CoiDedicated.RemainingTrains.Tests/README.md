# Remaining train command regression

Build against the installed CoI 0.8.7d assemblies into an isolated artifacts directory:

```powershell
dotnet build tests/CoiDedicated.RemainingTrains.Tests/CoiDedicated.RemainingTrains.Tests.csproj --artifacts-path .work/train-regression-build
dotnet .work/train-regression-build/bin/CoiDedicated.RemainingTrains.Tests/debug/CoiDedicated.RemainingTrains.Tests.dll --unit
dotnet .work/train-regression-build/bin/CoiDedicated.RemainingTrains.Tests/debug/CoiDedicated.RemainingTrains.Tests.dll ABSOLUTE_SOURCE_SAVE ABSOLUTE_NEW_OUTPUT_DIR
```

`COI_GAME_DIR` overrides the local game installation. The native run uses the production host's genuine Steam startup validation and requires a valid local Steam session. It copies the save, refuses an existing output directory, and never connects to the live manager/server.

The unit mode exercises 28 representative encodes covering nullable fields, color payloads, all five allowed pathfinding-restriction types, every truncated prefix/trailing bytes, invalid indices/enums and bounded allocation/discriminator rejection. BuildTrain's runtime-dependent depot is covered in the native fixture instead.

The native fixture verifies all 25 new concrete command classes (wire kinds350–374), plus four existing fixture setup commands. Every invocation checks canonical re-encoding and native processed/result success. It checks observable state for departure conditions (including explicit product clear and reorder), restriction lists, train build queue, rail direction and pillars, cargo filters, maintenance restoration, recovery pause, and stored wagon removal. World-reference tests reject wrong condition type, stale condition/restriction index, track/pillar block overflow, locomotive-as-cargo and subcar overflow.

Fixtures unlock required prototypes and create completed stations/depot and train cars using native public fixture APIs only within the disposable copy. BuildTrain itself executes the ordinary native construction command and leaves its train queued; it does not receive free construction. Recovery uses a genuinely spawned fixture train. Native economic checks remain active. Test helpers are not protocol commands.

The successful run at `.work/remaining-trains/native-e` had 45 native invocations / 29 distinct classes (25 new), 7 world-reference rejections, and zero native error logs. This establishes native processor and codec behavior, not two-client Unity UI/end-to-end train pathfinding coverage.
