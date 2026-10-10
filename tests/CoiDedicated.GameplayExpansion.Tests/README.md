# Native gameplay expansion fixture

This executable loads a **copy** of an existing CoI 0.8.7d island through the production headless host. It never writes the original save. An admission regression briefly starts a server on an ephemeral loopback port and injects fixture proposals locally; it does not connect real players. Use a fresh output directory for every run.

```powershell
dotnet run --project tests/CoiDedicated.GameplayExpansion.Tests -c Release --artifacts-path .work/gameplay-native-build -- path/to/island.save .work/gameplay-native-run
```

The optional third argument is the local Steam game directory. The default is `F:\SteamLibrary\steamapps\common\Captain of Industry`; `COI_GAME_DIR` also overrides it. Proprietary game DLLs are loaded from that installation and are not redistributed.

The fixture needs an island with an existing static building and truck, plus space around the starting colony. It unlocks selected prototypes **only inside the isolated test world** to exercise roads, decals, bridges, rail and inspector controls without waiting for research. It creates ordinary unfinished buildings through their native commands. A completed train station is created only as fixture setup for schedule editing.

Each exercised command is encoded, decoded against the current world, re-encoded to check retained fields, and scheduled through the real input scheduler. The test checks native completion and error status, plus selected actual effects: road designation insertion/removal, zone name and truck assignment links, zone deletion, unfinished bridge/track construction and rail direction, train schedule insertion/removal, and line/network deletion.

The evidence is written to `gameplay-expansion-results.json`. A successful run covers road and decal tools, zone management/truck work rules, storage/farm/forestry/depot inspectors, one bridge entrance with lane clearing, one ground track, train line/network lifecycle and basic schedule flags/reordering/removal.

The schedule regression rejects unrelated entity/root references, then queues an ordinary command, a last-root removal and a now-stale schedule removal. It checks that the first two are admitted on separate ticks and the stale third proposal is rejected against the updated world with no leaked queue count. A second queue scenario moves a schedule item before its priority mutation and checks that now-stale station membership is rejected. This protects native list-index and membership operations from concurrent stale proposals. The ground-rail fixture also confirms that the advisory `PolesValid` planner flag does not block native-supported geometry; it does not exercise electrified rail.

This is not proof of every rail or bridge shape. Elevated rail pillars, electrification poles, multi-segment bridge plans, running train assignment and every detailed inspector variant still require additional fixtures and in-game checks. The adapter unit suite separately checks malformed counts, IDs, indices, enums, percentage precision, truncated inputs and trailing bytes.
