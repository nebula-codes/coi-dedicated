# Process lifecycle tests

Run on Linux with the .NET 10 SDK:

```sh
dotnet run --project tests/CoiDedicated.Lifecycle.Tests
```

The harness launches isolated child processes and sends actual SIGTERM/SIGINT
signals. It verifies that the production `ShutdownSignal` allows finalization
and successful exit, that repeated SIGTERM does not interrupt finalization, and
that disposing the registration restores default process termination. It does
not load the game or prove that an island is saved; Docker integration tests
must separately stop and restart a server using real game files.

Windows builds the same harness but skips POSIX tests. The Windows console path
continues to use `Console.CancelKeyPress`; this harness does not synthesize
Windows console control events.
