# Multiplayer functionality coverage

Alpha.5 targets CoI **0.8.7d**, compatibility **dedicated-prototype-5 / cmd1**. It is not full gameplay parity. Earlier public releases and local alpha.3/alpha.4 candidates cannot join a prototype 5 server. Upgrade every client and server together.

The adapter now covers **199 command classes through 200 wire schemas** (the configured-building variant has its own schema). Static inspection found **328 concrete game commands**, with **282** observed constructor callers in Unity code; these totals also include UI-only, developer, and internal actions. They are not a completion percentage. The complete machine-readable ledger is [multiplayer-command-coverage.json](https://github.com/nebula-codes/coi-dedicated/blob/v0.1.0-alpha.5/research/multiplayer-command-coverage.json). It distinguishes implemented codecs, native processor fixtures, and replay evidence; 87 commands with observed UI callers still lack a codec.

## Implemented paths

- Native building-menu batches for ordinary player-buildable layout buildings, up to 128 items. Normal construction costs, unlock checks, placement validation, mini-zippers, result objects, and callback timing are preserved.
- Typed copied settings for ordinary machines/storage: recipes, products, storage percentages/alerts/truck groups, pause, logistics, priorities and titles. Unknown fields and external assignment graphs fail explicitly. Mixed complex blueprints and specialized configurations are not accepted as opaque game blobs.
- Ruins scrapping, fleet crew loading/unloading, tree harvesting selections, vehicle navigation, and entity pause toggles.
- Inspector add/remove vehicle controls, including assigning trucks to tree harvesters, preserve the selected vehicle type, count (up to 128), and optional logistics zone. Native game logic chooses available compatible vehicles. Individual vehicle assignment/unassignment remains supported.
- Belt/pipe transport construction schema; mining/dumping designations and removals; mining/forestry work areas; dump-product selection.
- Road/surface designation add/remove, decal add/remove/color, bridge plans/lane settings, rail plans, logistics zone lifecycle and truck work rules. Rail support geometry is rebuilt from trusted prototypes and current terrain.
- Train line/network naming and lifecycle, basic schedules and station membership, schedule flags/reordering, refueling thresholds and assorted train controls. These schemas do not imply every rail shape, running-train configuration, or schedule variant has been played through.
- Research queue/start/stop, edicts, contracts/trading, shipyard repair, crop schedule changes, storage products/alerts/logistics/sliders, depot build queues, assignments, priorities, naming, and many scalar factory/vehicle/ship controls. A schema being implemented does not mean every UI variant is tested.
- Camera focus labels including off-screen locations, approximate camera view rectangles, and outlines for buildings, transports, rail plans, paving and terrain tools. These are cosmetic state, separate from authoritative simulation commands. F8 provides a player name and show/share toggle.
- Shared speed 1–4 with fixed native step budgeting, selected-state checkpoints and bounded automatic fresh-snapshot recovery. F8 remembers connection details across game/PC restarts; key contents are never copied into settings.

## Validation completed

- Unit checks cover explicit wire fixtures, bounds, truncation, invalid versions/booleans/UTF-8, unknown kinds, invalid plans, typed references, and prohibited placement flags.
- A copied island accepted native batch placements for nine prototype families: ordinary machine, unit/loose/fluid storage, research lab, vehicle depot, farm, mining tower, and forestry tower. Duplicate placement and original native completion timing were checked.
- Native processor tests exercised the six originally blocked early-game actions plus storage, planning, farm, work-area, and terrain controls. The JSON ledger distinguishes these from untested schemas.
- A copied live island passed eight truck-assignment scenarios using an existing tree harvester. Tests checked actual two-truck add/remove ownership links, direct individual assignment/unassignment, zone-filtered selection, native zone release behavior, preserved large zone IDs, and rejection of invalid entities/prototypes.
- Two separate simulation peers replayed 100 shared ticks including a native batch. Both ended at step 27812, sequence 1176, with identical serialized world-state SHA-256 `aafe8e2e3b6fb6e3da9ae7264aeb192dad31e498cc3eb0004641a5fed4bc69f1`.
- A two-peer network test verified camera/outline relay, hidden presence, disconnect removal, and three-second expiration while ticks continued. Protocol tests reject nonfinite coordinates, excessive previews, malformed frames, and invalid identities.
- The new copied-island fixture passed 60 native command invocations spanning roads/decals, zones/truck rules, storage/farm/forestry/depot controls, one bridge entrance and lane clearing, one unfinished ground rail with direction preserved, and train line/network/schedule editing, including same-tick admission regressions.
- A separate native blueprint fixture passed storage and machine placement with copied settings, existing-building identity behavior, malformed/truncated payload rejection and unsupported-configuration rejection.
- Two native peers each replayed 1,200 authoritative updates, six matching checkpoints, speed transitions 1–4, pause/resume and building at 4×. Final raw serializations matched. Actual local entity-state divergence was detected; fresh same-name snapshot rejoin passed another 400 updates. See [synchronization measurements](https://github.com/nebula-codes/coi-dedicated/blob/v0.1.0-alpha.5/docs/synchronization.md) for scope and performance.
- A further five-minute revision-5 soak passed 3,000 updates and 11 checkpoints per peer, with matching final raw serialization. A dormant-session regression verifies that a server with no active players skips terrain hashes and network frame construction while advancing its authoritative sequence.
- Actual BepInEx config save/reload, bounded reconnect policy, and five presence geometry/installed-field contract checks pass. These checks do not execute Unity scene transitions or prove the overlay looks correct in-game.

These are bounded fixture results, not proof of long-session determinism. Actual Unity rendering and two people using all menus remain to be tested for this update. Building these sources does not automatically upgrade an existing server or installed client.

## Remaining work

Remaining gaps include mixed/specialized blueprints and external configuration graphs, advanced train departure conditions/filters and some train operations, bulk decal/surface tools, world-map/loan/cargo operations and specialized numeric inspectors listed below. Elevated/electrified rails and multi-segment bridge plans have codecs but still need native/in-game variant tests. Speeds above 4 are intentionally rejected.

Checkpoints cover selected canonical fields, not every queue, route, buffer or private entity state. The terrain scan can briefly pause play; large-island and multi-hour sessions need further measurement. Automatic Unity scene reloading and two-player overlay visuals require an actual in-game check.

Presence shows outlines, not textured 3D ghosts. It includes up to 32 previews per player, eight players, and five updates per second. Removal tools, rail-junction overlays and copied graphs outside active tools are not represented. Camera view rectangles use an approximate horizontal plane.

## Commands with observed UI callers still lacking a codec

- `Mafi.Base.Prototypes.Buildings.SetBillboardColorCmd`
- `Mafi.Base.Prototypes.Trains.TrainStationFuelSetFuelCmd`
- `Mafi.Core.Buildings.Cargo.Modules.CargoDepotAssignContractCmd`
- `Mafi.Core.Buildings.Cargo.Modules.CargoShipDepartNowCmd`
- `Mafi.Core.Buildings.Cargo.Modules.CargoShipPayWithUnityIfOutOfFuelCmd`
- `Mafi.Core.Buildings.Cargo.Modules.CargoShipSetFuelSaverCmd`
- `Mafi.Core.Buildings.Farms.AnimalFarmSetSlaughterLimitCmd`
- `Mafi.Core.Buildings.Farms.MoveAnimalsCmd`
- `Mafi.Core.Buildings.Mine.AddProductToNotifyIfCannotDumpCmd`
- `Mafi.Core.Buildings.Mine.RemoveProductToNotifyIfCannotDumpCmd`
- `Mafi.Core.Buildings.Offices.SetOfficeComputingBoostStepCmd`
- `Mafi.Core.Buildings.Offices.SetOfficeFocusStepCmd`
- `Mafi.Core.Buildings.Shipyard.ShipyardWorldEntityConstructionToggle`
- `Mafi.Core.Buildings.Storages.StorageCheatClearProductCmd`
- `Mafi.Core.Buildings.Storages.StorageSetCheatModeCmd`
- `Mafi.Core.Entities.Commands.SetDumpHeightOffsetCmd`
- `Mafi.Core.Entities.ReplaceEntityCmd`
- `Mafi.Core.Entities.Static.Commands.CutEntitiesForPasteCmd`
- `Mafi.Core.Entities.Static.Commands.FinishBuildOfStaticEntityCmd`
- `Mafi.Core.Entities.Static.Commands.SpendUpointsOnEntitiesCmd`
- `Mafi.Core.Entities.Static.Commands.StartDeconstructionOfTransportSubSectionsCmd`
- `Mafi.Core.Entities.Static.RecoverOceanAccessCmd`
- `Mafi.Core.Entities.ToggleEnabledGroupCmd`
- `Mafi.Core.Factory.Datacenters.DataCenterToggleRackCmd`
- `Mafi.Core.Factory.ElectricPower.SetElectricityGenerationPriorityCmd`
- `Mafi.Core.Factory.NuclearReactors.NuclearReactorSetEnrichmentStepCmd`
- `Mafi.Core.Factory.NuclearReactors.NuclearReactorSetPowerLevelCmd`
- `Mafi.Core.Factory.Transports.AddTransportPillarCmd`
- `Mafi.Core.Factory.Transports.DeconstructTransportSegmentCmd`
- `Mafi.Core.Factory.Transports.StackerTowerSelectProductForSlopePreviewCmd`
- `Mafi.Core.Factory.Transports.StackerTowerSetDumpRadiusCmd`
- `Mafi.Core.Factory.Transports.StackerTowerSetMaxSegmentsCmd`
- `Mafi.Core.Factory.Zippers.ZipperSetPriorityPortsCmd`
- `Mafi.Core.Game.ChangeGameDifficultyCmd`
- `Mafi.Core.MessageNotifications.MessageNotificationDismissCmd`
- `Mafi.Core.Notifications.NotificationDismissCmd`
- `Mafi.Core.Products.PinnedProductReorderCmd`
- `Mafi.Core.SpaceProgram.BringAsteroidToOrbitCmd`
- `Mafi.Core.SpaceProgram.DiscardAsteroidCmd`
- `Mafi.Core.SpaceProgram.DropAsteroidAtCmd`
- `Mafi.Core.SpaceProgram.ReorderLaunchPadCargoBufferCmd`
- `Mafi.Core.SpaceProgram.SetOrbitCargoLimitCmd`
- `Mafi.Core.Terrain.Designation.BatchAddSurfaceDecalCmd`
- `Mafi.Core.Terrain.Designation.BatchRemoveSurfaceDecalCmd`
- `Mafi.Core.Terrain.Designation.BatchRemoveSurfacePlacingDesignationsCmd`
- `Mafi.Core.Terrain.Designation.PasteSurfaceDesignationsCmd`
- `Mafi.Core.Terrain.Designation.QuickFulfillDesignationsCmd`
- `Mafi.Core.Terrain.Props.QuickRemovePropsCmd`
- `Mafi.Core.Terrain.Trees.RemoveManualPlantTreeCmd`
- `Mafi.Core.Trains.AddTrainTrackPillarCmd`
- `Mafi.Core.Trains.BuildTrainCmd`
- `Mafi.Core.Trains.EditPathfindingRestrictionCmd`
- `Mafi.Core.Trains.QuickRepairTrainCmd`
- `Mafi.Core.Trains.RecoverTrainCmd`
- `Mafi.Core.Trains.RemoveDepartConditionCmd`
- `Mafi.Core.Trains.RemovePathfindingRestrictionCmd`
- `Mafi.Core.Trains.ReorderDepartConditionCmd`
- `Mafi.Core.Trains.ReplaceEntityInScheduleCmd`
- `Mafi.Core.Trains.ReverseTracksCmd`
- `Mafi.Core.Trains.ScrapTrainCarCmd`
- `Mafi.Core.Trains.SetDepartConditionCombineMethodCmd`
- `Mafi.Core.Trains.SetTrainLineColorCmd`
- `Mafi.Core.Trains.SetTrainLineIcon`
- `Mafi.Core.Trains.SetTrainLineLocoColorModeCmd`
- `Mafi.Core.Trains.SetTrainLineLocoCustomColorCmd`
- `Mafi.Core.Trains.SetTrainNetworkColorCmd`
- `Mafi.Core.Trains.SetTrainNetworkLocoColorModeCmd`
- `Mafi.Core.Trains.SetTrainNetworkLocoCustomColorCmd`
- `Mafi.Core.Trains.SetTrainTrackCriticalBlockStateCmd`
- `Mafi.Core.Trains.SetTrainTrackSuperBlockStateCmd`
- `Mafi.Core.Trains.SetWagonProductFilterCmd`
- `Mafi.Core.Trains.ToggleBidirectionalCommand`
- `Mafi.Core.Trains.TrainContainsDepartConditionEditCmd`
- `Mafi.Core.Trains.TrainDurationDepartConditionEditCmd`
- `Mafi.Core.Vehicles.AddVehicleReplacementTaskCmd`
- `Mafi.Core.Vehicles.Commands.ReplaceVehicleCmd`
- `Mafi.Core.Vehicles.RemoveVehicleReplacementTaskCmd`
- `Mafi.Core.World.Entities.SetShiftsCountForMineCmd`
- `Mafi.Core.World.FleetModificationsPrepareCmd`
- `Mafi.Core.World.GoToLocationCmd`
- `Mafi.Core.World.Loans.AcceptLoanCmd`
- `Mafi.Core.World.Loans.MakeLoanOverpaymentCmd`
- `Mafi.Core.World.Loans.SetLoanBufferPriorityCmd`
- `Mafi.Core.World.WorldMapEntityCancelRepairCmd`
- `Mafi.Core.World.WorldMapEntityStartRepairCmd`
- `Mafi.Core.World.WorldMapEntityUpgradeCmd`
- `Mafi.Core.World.WorldMapSettlementAdoptPopsCmd`
