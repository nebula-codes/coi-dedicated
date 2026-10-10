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

Payloads start with uint16 schema version `1`, followed by a stable uint16 command ID. All integers are little-endian. Each command is limited to 64 KiB; the server admits at most 512 KiB of command data plus overhead per tick and defers the remaining proposals in order. Strings are strict UTF-8 with bounded uint32 byte lengths.

Compatibility revision **dedicated-prototype-5** covers the expanded command semantics and presence protocol, alongside the exact hashes of the three simulation assemblies. All peers must upgrade together. See [the coverage ledger](../../docs/multiplayer-coverage.md) for the complete distinction between implemented codecs, native tests, and remaining work.

The codec covers 199 command classes through 200 explicit schemas. IDs 1-8 cover original controls and native building batches; 20-25 early-game actions and transports; 40-46 research/trading; 60-65 terrain/work areas; 70-80 management; 81-88 surfaces/bridges/rail; 100-252 explicit scalar/reference, logistics, inspector and train controls; 400 typed copied building settings. The scalar tables are compile-time allowlists of exact command classes and constructors. Network data never specifies a CLR type, constructor, reflection path, or opaque game serializer payload.

Every decode produces a fresh, unscheduled command. Parsing checks bounded values and trailing bytes before resolving live references; typed prototype/entity validation follows. The native game processor performs its normal final placement, cost, and gameplay checks at the ordered tick. Local author clients retain their original input object when the server echoes identical bytes, preserving native UI result and completion behavior.

Native building batches support up to 128 ordinary layout buildings, preserving transforms, reflection, and mini-zipper settings. Free construction, validation suppression and special rail fields in ordinary layout batches are rejected explicitly. The separate typed copied-settings schema accepts ordinary machine/storage settings and rejects unknown fields or external assignment graphs; rail plans use their own bounded schema. Transport plans are bounded to 1024 pivots/hints; mining plans to 1024 aligned designations; work areas to 256 vertices. These bounds and support restrictions are intentional and visible to the player.

The presence codec lives in the protocol project and is independent of the command allowlist. It conveys display hints only and cannot schedule construction or mutate the island.

Build and test from the repository root:

```powershell
dotnet build src/CoiDedicated.GameAdapter --configuration Release
dotnet run --project src/CoiDedicated.GameAdapter/Tests/CoiDedicated.GameAdapter.Tests.csproj --configuration Release
```

Set `-p:GameDir=...` to build against another installation. Tests accept the game directory as their first positional argument or `COI_GAME_DIR`; the default is this machine's installed game. Tests use command constructors and an empty/mocked dependency resolver, without creating or launching a game world. They verify fixed byte fixtures, malformed input rejection, unsupported-command reporting, and rejection of missing entities. Valid world-specific entity/recipe operations still require host integration tests.
