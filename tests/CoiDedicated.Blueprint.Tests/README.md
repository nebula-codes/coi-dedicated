# Typed blueprint settings integration

```powershell
dotnet run --project tests/CoiDedicated.Blueprint.Tests -c Release --artifacts-path .work/blueprint-native-build -- path/to/island.save .work/new-blueprint-test
```

The fixture copies the supplied save into a new directory and runs the real headless game there. It requires an ordinary unit storage and a configured ordinary machine with a recipe in that island. It never changes the original save or controls the live server.

Wire kind 400 carries configured `BatchCreateStaticEntitiesCmd` placements for supported `MachineProto` and `StorageProto` instances. Kind 8 remains the original placement-only schema. Fixed typed fields cover common pause, logistics, priority and title settings, machine recipe IDs, and storage product, percentages, alerts and truck-group IDs. Each request has at most 128 buildings and each prototype array at most 64 entries under the existing 64 KiB command limit. There are no recursive nodes, transmitted dictionary keys, game blobs, or network-selected CLR types.

Tests check native new construction with storage settings, machine recipes/pause/priority, native existing-building identity handling, canonical wire reconstruction, all truncations and trailing bytes, and rejection of unknown configuration, external assignment graphs, cheat fields, invalid percentages/modes, duplicate source IDs, free construction and oversize arrays.

Normal placement/research/cost checks remain active. Native storage rules still make truck import and export sliders mutually exclusive; wire preservation does not override game behavior. Source configuration keys outside the whitelist fail explicitly instead of being silently dropped. Mixed blueprints involving transports, rails, farms, modules, assignments to other entities, attached pillars or specialized machine settings remain unsupported by this schema. The separate existing `CloneConfigBetweenEntitiesCmd` still covers native settings copying between existing entities.

Discovery reused the retained `Mafi.Core.dll` artifact inventory and focused execution-free ILSpy reads of `EntityConfigData`, `ConfigSerializationContext`, `EntitiesCloneConfigHelper`, `EntitiesCommandsProcessor`, `Machine`, and storage configuration methods under ignored `.work/blueprint-config`. Native tests, rather than those static reads, establish the integration behavior.
