# Dedicated host control client

This .NET 10 console client sends administrative commands to the dedicated host. It is not a playable game client.

```powershell
dotnet run --project src/CoiDedicated.Control -- --secret-file server-data/control.secret --command status
dotnet run --project src/CoiDedicated.Control -- --secret-file server-data/control.secret --command pause
dotnet run --project src/CoiDedicated.Control -- --secret-file server-data/control.secret --command snapshot --output server-data/downloaded-snapshot.bin
```

Options: `--host` (default `127.0.0.1`), `--port` (default `28550`), `--name` (default `coi-control`), `--secret-file` (required), `--command` (default `status`), and `--output` (required only for `snapshot`). Commands are `status`, `pause`, `resume`, `save`, `snapshot`, and `shutdown`. All options use separate values, such as `--port 28550`.

The secret file is UTF-8 text; trailing CR/LF characters are ignored. The client never prints the secret and sends an HMAC-SHA256 challenge response rather than the secret itself. This challenge authenticates the control client to the host; the TCP transport itself is not encrypted.

The entire connection/command has a 30-second deadline, extended to 120 seconds for snapshots. Ctrl+C cancels the operation. Success exits with code 0; rejected, malformed, disconnected, or timed-out operations exit with code 1.

Snapshots are limited to 256 MiB and downloaded to a sibling `<output>.tmp` file. The client verifies the advertised byte length, SHA-256 digest, and terminating frame before renaming the file to the requested destination. Existing destination or staging files are preserved. A failed download removes only the temporary file created by that operation.

Wire compatibility: stage-0 Welcome contains a one-byte `0`, a 32-byte nonce field, and build string (128-byte limit); Hello contains client name (64-byte limit) and 32-byte proof field; stage-1 Welcome contains one-byte `1` and build string. Command contains uint32 request ID and command string. Ack contains matching uint32 ID, Boolean success, and response string (65536-byte limit). SnapshotBegin contains int32 step, int64 length, and 32-byte SHA-256 field. SnapshotChunk contains a byte-array field of at most 65536 bytes; SnapshotEnd has an empty payload. Error contains one response string.

Independent loopback fixture tests use Node built-ins to verify the HMAC wire format, status response, snapshot download, and corrupt-snapshot cleanup without a running game host:

```powershell
dotnet build src/CoiDedicated.Control --configuration Release
node src/CoiDedicated.Control/Tests/control-smoke.mjs
```
