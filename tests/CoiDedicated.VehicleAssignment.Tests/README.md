# Native vehicle-assignment fixture

This executable copies an existing CoI 0.8.7d save into a new output directory,
loads that copy through the production headless `GameHost`, and runs assignment
commands through the production codec and native input scheduler. It does not
connect to or modify the running server. The fixture needs a tree harvester and
at least two compatible trucks of the same prototype.

```powershell
dotnet run --project tests/CoiDedicated.VehicleAssignment.Tests -c Release --artifacts-path .work/vehicle-assignment-build -- SOURCE_SAVE NEW_OUTPUT_DIRECTORY
```

The checks cover type-based count-two assignment/removal, specific-vehicle
assignment/removal, both directions of the assignment links, explicit logistics
zone filtering, optional-zone preservation, large zone IDs, and invalid target
and prototype rejection. Each command receives two native runner updates to
process and synchronize completion. Existing assignments and truck zones are
changed only in the copied fixture. Test-created zones use the native zone
manager solely for fixture setup.

The native removal processor treats a present zone option as a request to return
the truck to the default zone; this test preserves and checks that behavior. It
does not claim relocation to the named optional zone. Results are written to
`vehicle-assignment-results.json` in the selected output directory. This is a
headless native integration test, not a Unity UI or two-client replay test.
