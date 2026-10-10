# CoI dedicated protocol framing

This dependency-free `netstandard2.1` library provides framing and explicit primitive payload encoding shared by a server and a compatible client. It does not implement game synchronization or imply that a multiplayer session works.

## Wire format, version 1

All integers are little-endian. A frame contains:

| Bytes | Field |
| --- | --- |
| 4 | Unsigned body length; excludes this prefix |
| 2 | Protocol version, currently `1` |
| 2 | `MessageKind` |
| Remainder | Payload defined by that message's application schema |

The body length must be `4..1048576` bytes, making the maximum payload `1048572` bytes. The length is validated before payload allocation. Both known message kinds and version are validated before payload reading. Payloads can be empty; individual message handlers decide if an empty payload is meaningful.

The stable kind IDs are Hello=1, Welcome=2, SnapshotBegin=3, SnapshotChunk=4, SnapshotEnd=5, Command=6, Tick=7, Ack=8, Error=9, Ping=10, CommandRejected=11.

Payload strings and byte arrays have their own unsigned 4-byte byte-length prefix. Strings use strict UTF-8, with a default 4096-byte limit. Readers and writers support a smaller per-field limit. Boolean encoding is exactly one byte, `0` or `1`. The framing library does not deserialize object graphs. Game-command payloads use an explicit allowlist; server snapshots are opaque transfers subsequently loaded by the game's native save loader.

## Gameplay session

The player endpoint uses a fresh HMAC challenge and a distinct join key. After authentication it sends a snapshot automatically. The client validates the length and SHA-256, loads the island, and sends `Ack(Int32 snapshotStep)`. The host holds updates during this join barrier, with a bounded deadline.

Live messages use these payloads:

| Direction / kind | Payload |
| --- | --- |
| Client `Command` | UInt64 increasing request ID; Bytes encoded command |
| Server `Tick` | UInt64 sequence; Int32 before step; Int32 after step; Boolean **before-update** pause state; UInt16 command count; each command contains UInt64 request ID, String author, Bytes encoded command |
| Client `Ack` after ready | UInt64 tick sequence; Int32 actual resulting step |
| Server `CommandRejected` | UInt64 request ID; String reason; this rejects one proposal without closing the session |
| Server `Error` | String fatal error |

The host reserves request ID zero and author `server` for administrative commands. Clients set the supplied pre-update pause state, schedule admitted commands in order, and call one 100 ms game update per tick. Current gameplay sessions use speed 1. Step acknowledgments detect skipped or duplicated updates; they are not full-state checksums.

## API

```csharp
using CoiDedicated.Protocol;

// These illustrative fields are not a predefined Hello schema.
using var payload = new PayloadWriter();
payload.WriteString("client-name", maxByteLength: 128);
payload.WriteUInt64(42);
await FrameCodec.WriteAsync(stream, payload.ToFrame(MessageKind.Hello), cancellationToken);

ProtocolFrame? frame = await FrameCodec.ReadAsync(stream, cancellationToken);
if (frame == null) return; // Clean EOF between frames.

if (frame.Kind != MessageKind.Hello)
    throw new ProtocolException("Expected Hello.");
var reader = new PayloadReader(frame.Payload);
string name = reader.ReadString(maxByteLength: 128);
ulong value = reader.ReadUInt64();
reader.EnsureEnd();
```

Primitive operations are `Read/WriteBoolean`, `UInt16`, `UInt32`, `UInt64`, `Int32`, `Int64`, `String`, and `Bytes`. `PayloadReader.Position` and `Remaining` support diagnostics; `EnsureEnd` rejects unknown trailing data. A `ProtocolFrame` public constructor copies its payload.

`ReadAsync` handles arbitrary stream fragmentation. It returns null only at clean EOF before any length-prefix byte. EOF partway through a frame throws `EndOfStreamException`; malformed complete data throws `ProtocolException`. Local invalid API arguments throw standard argument exceptions. Exceeding a writer's total bound throws `InvalidOperationException` without partially writing that field. Close the connection after a framing or payload error; there is no resynchronization mechanism.

The caller owns the stream and must serialize writes, run only one reader, and enforce connection deadlines and cancellation. `WriteAsync` neither flushes nor closes the stream. A canceled or failed write can leave a partial frame, so the connection must be discarded. The library supplies no authentication, transport encryption, per-client queue bounds, replay protection, compression, or command authorization; those belong to the host/session layer.

## Verification

From the repository root:

```powershell
dotnet run --project tests/CoiDedicated.Protocol.Tests/CoiDedicated.Protocol.Tests.csproj --configuration Release
```

The console selftests exercise fragmented reads, consecutive messages, maximum-size frames, empty payloads, clean and truncated EOF, oversized lengths, unsupported versions/kinds, cancellation, typed fields, invalid UTF-8, and atomic writer bounds.
