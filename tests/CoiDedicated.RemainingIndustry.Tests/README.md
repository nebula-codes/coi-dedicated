# Remaining industry and surface command checks

Requires the supported, unmodified Captain of Industry 0.8.7d installation, .NET 10, and the owning Steam account running on Windows for native island tests. Set `COI_GAME_DIR` when the installation is not in the default Steam library. The project references local game assemblies without copying them into test output.

Build into a scratch directory, then run the codec fixtures:

```powershell
dotnet build tests/CoiDedicated.RemainingIndustry.Tests --artifacts-path .work/industry-tests --nologo
dotnet .work/industry-tests/bin/CoiDedicated.RemainingIndustry.Tests/debug/CoiDedicated.RemainingIndustry.Tests.dll
```

To run native processor checks, supply a compatible existing save and a **new** output directory:

```powershell
dotnet .work/industry-tests/bin/CoiDedicated.RemainingIndustry.Tests/debug/CoiDedicated.RemainingIndustry.Tests.dll C:/fixtures/island.save .work/industry-native-run
```

The test copies the save before loading it and never edits the source save, manager installation, or running server. It initializes the normal native simulation through `GameHost` and genuine Steam ownership validation. `results.json` records each native invocation and whether the game returned a normal error; an error-result invocation is not evidence of a successful game action.

The codec suite covers all 30 industry schemas (IDs 300–329), the four surface batch schemas (435–438), every fixture's truncated and trailing payloads, invalid scalar/enum ranges, packed surface reserved bits, duplicate origins/slots, and allocation bounds. Test-only reflection creates command field fixtures where the constructor normally needs a live entity; production encoding/decoding has no reflection or object-graph deserialization.

Native tests create isolated building fixtures, unlock their prototypes, materialize one generated world mine through its native factory, and create an unassigned cargo ship using its native constructor/registration path. The test seeds a normal `ActiveLoan` record to exercise repayment and priority on an early island without a trading dock. Network commands still pass through the codec, native input scheduler, and ordinary processors. Assertions cover field-preserving roundtrips, real surface designation/decal add/remove, typed reference rejection, reactor/office bounds, bulk office/rack controls, native loan eligibility, limited repayment, and construction/resource checks.

Coverage depends on available native fixtures. The coastal cargo-depot fixture uses a bounded search and reports an explicit skip if no valid placement exists. An early island can correctly reject ship travel, individual vehicle replacement, and repair of an already repaired location. These tests do not prove completed long-running construction, ship travel, multiplayer UI behavior, or every late-game state.

Fleet modification requests use bounded explicit slot IDs and optional part IDs. Parts must be unlocked and eligible for the current ship's slots. Native pricing and construction remain authoritative. The current shipped ship designer emits a null hull ID; arbitrary replacement hulls and removal of installed parts through the incompatible native pricing path are rejected. Surface batches preserve explicit packed scalar fields, validate snapshot prototype IDs and map bounds, and are limited to 2,048 records and 16,384 total tiles.
