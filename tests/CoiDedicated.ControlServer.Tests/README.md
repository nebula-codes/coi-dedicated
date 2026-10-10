# Control snapshot regression tests

Run `dotnet run --project tests/CoiDedicated.ControlServer.Tests` from the repository root. The test project links the production control server and protocol library; it does not need game assemblies.

The loopback tests verify a snapshot retains its original bytes and step through two save rotations, a canceled completion disposes the opened file, a disconnected client disposes its pending reply, and an ordinary save access failure produces a protocol error. The save-rotation case includes the Windows behavior that requires deleting the previous backup before replacing it while a download still holds that generation open.
