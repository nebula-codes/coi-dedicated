# Native building-batch integration

Runs the production headless loader against a read-only CoI 0.8.7d fixture save. Use a populated island and a **new output directory**. The test does not contact or restart a running server. All test-world saves go into the output directory.

```powershell
dotnet run --project tests/CoiDedicated.BuildingPlacement.Tests -- `
  C:\fixtures\island.save C:\test-output\building-batch-1 `
  "F:\SteamLibrary\steamapps\common\Captain of Industry"
```

The fixture must have room around its starting factory. Tests place nine building families: an ordinary machine, unit/loose/fluid storage, a research lab, a vehicle depot, a farm, a mining tower, and a forestry tower through the real `BatchCreateStaticEntitiesCmd` scheduler and processor. Research-locked test prototypes are first checked for rejection, then unlocked only inside the isolated fixture. Assertions check normal unfinished construction, fresh command state, native completion and synchronization, exact encode/decode round trips, duplicate-placement identity handling, bounds, mini-zipper modes, and rejection of unsupported copied settings or specialized rail data.

`building-results.json` records the exact tested prototype names and results. `gameplay-results.json` records native early-game, storage, planning, crop scheduling, vehicle build queue, work-area, and terrain command results. This headless integration is separate from testing actual build-menu clicks, UI callbacks, and two connected Unity clients.
