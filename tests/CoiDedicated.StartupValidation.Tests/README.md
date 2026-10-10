# Native startup regression

This opt-in integration harness requires a locally owned Captain of Industry installation and a healthy year-10-or-later fixture with available unit storage research and vehicles that are not being scrapped. Run it against a copied test island, never a live server directory.

The test copies the fixture into a new output directory and loads `CoiDedicated.dll` from the explicit `--host-dir`. It calls the production `GameHost.Run`, places at least 120 ordinary unit-storage plans with native placement validation and input commands, verifies an entity ID divisible by 100 was created, and advances at least 3,100 actual simulation steps after placement. It allows normal population variation but rejects game-over, pirate logs, zero population, or unexpected vehicle scrapping. Event-registration inspection is read-only. No validation marker, ownership result, callback, or shipped game code is modified.

Build separately from release artifacts:

```powershell
dotnet build tests/CoiDedicated.StartupValidation.Tests --artifacts-path .work/startup-validation-build
dotnet .work/startup-validation-build/bin/CoiDedicated.StartupValidation.Tests/debug/CoiDedicated.StartupValidation.Tests.dll --source-save C:/fixtures/island.save --output C:/tests/startup-new --host-dir C:/builds/new-host
```

To prove the test detects the previous failure, run a separate process with the same fixture and a retained old host directory, adding `--expect-trigger`. That mode passes only if the old startup registers the callback and real building commands reproduce the native pirate game-over and population removal. Each run needs a fresh output directory and writes `results.json`, `native-game.log`, and any `failure.txt` there. Baseline mode stops once the expected destructive effect occurs; only the copied in-memory world is affected and the source fixture is never overwritten.

The harness does not work around unavailable Steam validation. A corrected host must perform its genuine native startup and report failure if its prerequisites are unavailable.
