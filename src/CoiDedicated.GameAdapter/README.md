# Explicit game-command adapter

This `netstandard2.1` library translates an allowlist of CoI input commands to bounded binary payloads. The local installed game assemblies provide the game types; this project does not copy or redistribute those assemblies.

```csharp
using CoiDedicated.GameAdapter;

if (!GameCommandCodec.TryEncode(command, out byte[] payload, out string reason))
    ShowUnsupportedCommand(reason);

// Run on the simulation thread immediately before scheduling.
IInputCommand decoded = GameCommandCodec.Decode(payload, resolver);
inputScheduler.ScheduleInputCmd(decoded);
```

Payloads start with uint16 schema version `1`, followed by uint16 command kind. All integers are little-endian. Total payload size is at most 512 bytes; strings are strict UTF-8 with the protocol library's uint32 byte-length prefix.

| Kind | Command | Explicit fields and validation |
| --- | --- | --- |
| 1 | `SetSimPauseStateCmd` | Boolean paused |
| 2 | `GameSpeedChangeCmd` | Int32 speed, `1..20` |
| 3 | `SetConstructionPriorityCmd` | Positive int32 entity ID; int32 priority `0..14`; existing live static entity |
| 4 | `MachineSetRecipeActiveCmd` | Positive int32 machine ID; recipe ID string, at most 256 bytes; Boolean enabled; live machine, existing available recipe, and recipe belongs to machine prototype |
| 5 | `StartDeconstructionOfStaticEntityCmd` | Positive int32 entity ID; Boolean area removal; live static entity; normal removal passes the game's removal validation |
| 6 | `ToggleStaticEntityConstructionCmd` | Positive int32 entity ID; live static entity that is not fully constructed |
| 7 (`CreateBuilding`) | `CreateStaticEntityCmd` for ordinary machines and standard storage | Prototype ID; int32 x/y/z and rotation `0..3`; Boolean reflection; available/unlocked exact `MachineProto`, `UnitStorageProto`, `LooseStorageProto`, or `FluidStorageProto`; permitted player construction and reflection; complete footprint inside playable map |

Deconstruction always uses `EntityRemoveReason.Remove`. Collapse is simulation-owned and cannot be requested through this codec. Only exact listed command types encode; subclasses and all other commands return an explicit unsupported reason. Payload parsing rejects trailing bytes, invalid Booleans, unknown versions/kinds, bad UTF-8, and invalid limits before resolving game state.

`Decode` produces a fresh, unscheduled command. Game processors still perform their normal final validation and may reject a command if state changes before execution. Authentication, user permissions, rate limiting, authoritative ordering, and error reconciliation are responsibilities of the session layer.

Building placement always uses normal construction costs and placement validation. Free-construction and validation-suppression flags do not exist in the wire schema; the encoder rejects commands that request either. The allowlist checks exact prototype types, so subclasses and specialized storage remain excluded. The `CreateBuilding` enum name replaces `CreateMachine`; its wire identifier remains `7` and its field layout is unchanged. Other building classes, batch placement, transports (including transport pillars), terrain editing, and broader management commands remain unsupported.

A live headless integration probe created separate unconstructed `SmokeStack`, `StorageUnit`, `StorageLoose`, and `StorageFluid` entities through the normal command processor. It first confirmed the three storages were rejected while research-locked, then unlocked each only in the isolated test world before placement. A transport-pillar placement remained rejected. This validates these placement paths; it does not validate all gameplay or storage management commands. The shared greeting uses compatibility revision `dedicated-prototype-2` to reject older adapters with different placement coverage, in addition to comparing the three simulation DLL hashes.

The ranges and field layouts were verified against installed CoI 0.8.7d command types, `SimLoopEvents.SetSimSpeed`, `GeneralPriorities.AssertAssignableRange`, and the corresponding game command processors using focused local decompilation. This does not establish that a complete multiplayer session remains deterministic.

Build and test from the repository root:

```powershell
dotnet build src/CoiDedicated.GameAdapter --configuration Release
dotnet run --project src/CoiDedicated.GameAdapter/Tests/CoiDedicated.GameAdapter.Tests.csproj --configuration Release
```

Set `-p:GameDir=...` to build against another installation. Tests accept the game directory as their first positional argument or `COI_GAME_DIR`; the default is this machine's installed game. Tests use command constructors and an empty/mocked dependency resolver, without creating or launching a game world. They verify fixed byte fixtures, malformed input rejection, unsupported-command reporting, and rejection of missing entities. Valid world-specific entity/recipe operations still require host integration tests.
