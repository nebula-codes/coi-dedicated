using System.Buffers.Binary;
using System.Text;
using CoiDedicated.Protocol;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Fragmented frames and typed fields round trip", RoundTripAsync),
    ("Every known kind accepts an empty payload", EmptyFramesAsync),
    ("Frame constructor copies payload", FrameCopiesPayloadAsync),
    ("Maximum frame round trip", MaximumFrameAsync),
    ("Oversized lengths rejected before payload reads", OversizedAsync),
    ("Short frame lengths rejected", ShortLengthsAsync),
    ("Truncated prefix, header, and payload rejected", TruncationAsync),
    ("Unknown versions and kinds rejected", UnknownHeadersAsync),
    ("Cancellation remains caller controlled", CancellationAsync),
    ("Malformed payload lengths, booleans, and UTF-8 rejected", MalformedPayloadAsync),
    ("Writer enforces byte bounds atomically", WriterBoundsAsync)
};
int failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} protocol tests passed.");
return failed == 0 ? 0 : 1;

static async Task RoundTripAsync()
{
    using var writer = new PayloadWriter();
    writer.WriteBoolean(true);
    writer.WriteBoolean(false);
    writer.WriteUInt16(65535);
    writer.WriteUInt32(uint.MaxValue);
    writer.WriteUInt64(ulong.MaxValue);
    writer.WriteInt32(int.MinValue);
    writer.WriteInt64(long.MinValue);
    writer.WriteString("Captain ⚓ U0001F30D");
    writer.WriteString("");
    writer.WriteBytes(new byte[] { 0, 1, 255 });
    writer.WriteBytes(Array.Empty<byte>());
    using var stream = new MemoryStream();
    await FrameCodec.WriteAsync(stream, writer.ToFrame(MessageKind.Hello));
    await FrameCodec.WriteAsync(stream, new ProtocolFrame(MessageKind.Ping, Array.Empty<byte>()));
    var wire = stream.ToArray();
    Equal((uint)writer.Length + 4, BinaryPrimitives.ReadUInt32LittleEndian(wire));
    Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(4)));
    Equal((ushort)MessageKind.Hello, BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(6)));
    using var fragmented = new FragmentedReadStream(wire, 1);
    var frame = await FrameCodec.ReadAsync(fragmented) ?? throw new Exception("Missing frame.");
    Equal(MessageKind.Hello, frame.Kind);
    var reader = new PayloadReader(frame.Payload);
    Equal(true, reader.ReadBoolean());
    Equal(false, reader.ReadBoolean());
    Equal(ushort.MaxValue, reader.ReadUInt16());
    Equal(uint.MaxValue, reader.ReadUInt32());
    Equal(ulong.MaxValue, reader.ReadUInt64());
    Equal(int.MinValue, reader.ReadInt32());
    Equal(long.MinValue, reader.ReadInt64());
    Equal("Captain ⚓ U0001F30D", reader.ReadString());
    Equal("", reader.ReadString());
    Check(reader.ReadBytes().SequenceEqual(new byte[] { 0, 1, 255 }), "Byte array mismatch.");
    Equal(0, reader.ReadBytes().Length);
    reader.EnsureEnd();
    Equal(MessageKind.Ping, (await FrameCodec.ReadAsync(fragmented))!.Kind);
    Check(await FrameCodec.ReadAsync(fragmented) == null, "Clean EOF should return null.");
}

static async Task EmptyFramesAsync()
{
    foreach (var kind in Enum.GetValues<MessageKind>())
    {
        using var stream = new MemoryStream();
        await FrameCodec.WriteAsync(stream, new ProtocolFrame(kind, Array.Empty<byte>()));
        stream.Position = 0;
        var frame = (await FrameCodec.ReadAsync(stream))!;
        Equal(kind, frame.Kind);
        Equal(0, frame.Payload.Length);
        new PayloadReader(frame.Payload).EnsureEnd();
    }
}

static Task FrameCopiesPayloadAsync()
{
    var input = new byte[] { 42 };
    var frame = new ProtocolFrame(MessageKind.Command, input);
    input[0] = 10;
    Equal((byte)42, frame.Payload.Span[0]);
    Throws<ArgumentOutOfRangeException>(() => new ProtocolFrame((MessageKind)0, input));
    Throws<ArgumentOutOfRangeException>(() => new ProtocolFrame(MessageKind.Command, new byte[FrameCodec.MaxPayloadLength + 1]));
    return Task.CompletedTask;
}

static async Task MaximumFrameAsync()
{
    var payload = new byte[FrameCodec.MaxPayloadLength];
    payload[0] = 42;
    payload[^1] = 43;
    using var stream = new MemoryStream();
    await FrameCodec.WriteAsync(stream, new ProtocolFrame(MessageKind.SnapshotChunk, payload));
    using var fragmented = new FragmentedReadStream(stream.ToArray(), 8191);
    var frame = (await FrameCodec.ReadAsync(fragmented))!;
    Check(frame.Payload.Span.SequenceEqual(payload), "Maximum payload changed.");
}

static async Task OversizedAsync()
{
    foreach (uint length in new uint[] { FrameCodec.MaxFrameBodyLength + 1, uint.MaxValue })
    {
        using var stream = new MemoryStream(Header(length, 1, MessageKind.Hello));
        await ThrowsAsync<ProtocolException>(() => FrameCodec.ReadAsync(stream));
        Equal(4L, stream.Position);
    }
}

static async Task ShortLengthsAsync()
{
    for (uint length = 0; length < FrameCodec.BodyHeaderLength; length++)
    {
        using var stream = new MemoryStream(Header(length, 1, MessageKind.Hello));
        await ThrowsAsync<ProtocolException>(() => FrameCodec.ReadAsync(stream));
        Equal(4L, stream.Position);
    }
}

static async Task TruncationAsync()
{
    var frame = Header(6, 1, MessageKind.Hello).Concat(new byte[] { 20, 21 }).ToArray();
    for (int count = 1; count < frame.Length; count++)
    {
        using var stream = new FragmentedReadStream(frame[..count], 2);
        await ThrowsAsync<EndOfStreamException>(() => FrameCodec.ReadAsync(stream));
    }
    using var empty = new MemoryStream();
    Check(await FrameCodec.ReadAsync(empty) == null, "Empty stream is clean EOF.");
}

static async Task UnknownHeadersAsync()
{
    foreach (var bytes in new[] { Header(4, 0, MessageKind.Hello), Header(4, 2, MessageKind.Hello), Header(4, 1, (MessageKind)0), Header(4, 1, (MessageKind)65535) })
    {
        using var stream = new MemoryStream(bytes);
        await ThrowsAsync<ProtocolException>(() => FrameCodec.ReadAsync(stream));
    }
}

static async Task CancellationAsync()
{
    using var source = new CancellationTokenSource();
    source.Cancel();
    using var stream = new MemoryStream();
    await ThrowsAsync<OperationCanceledException>(() => FrameCodec.ReadAsync(stream, source.Token));
    await ThrowsAsync<OperationCanceledException>(() => FrameCodec.WriteAsync(stream, new ProtocolFrame(MessageKind.Ping, Array.Empty<byte>()), source.Token));
    Equal(0L, stream.Length);
}

static Task MalformedPayloadAsync()
{
    Throws<ProtocolException>(() => new PayloadReader(new byte[] { 2 }).ReadBoolean());
    Throws<ProtocolException>(() => new PayloadReader(new byte[] { 1 }).ReadUInt32());
    Throws<ProtocolException>(() => new PayloadReader(new byte[] { 1 }).EnsureEnd());
    Throws<ProtocolException>(() => new PayloadReader(new byte[] { 255, 255, 255, 255 }).ReadBytes());
    Throws<ProtocolException>(() => new PayloadReader(new byte[] { 2, 0, 0, 0, 1 }).ReadBytes());
    Throws<ProtocolException>(() => new PayloadReader(new byte[] { 2, 0, 0, 0, 1, 2 }).ReadBytes(1));
    Throws<ProtocolException>(() => new PayloadReader(new byte[] { 1, 0, 0, 0, 255 }).ReadString());
    Throws<ProtocolException>(() => new PayloadReader(new byte[] { 2, 0, 0, 0, 0xC3, 0xA9 }).ReadString(1));
    Throws<ArgumentOutOfRangeException>(() => new PayloadReader(Array.Empty<byte>()).ReadBytes(-1));
    return Task.CompletedTask;
}

static Task WriterBoundsAsync()
{
    using var writer = new PayloadWriter(6);
    Throws<ArgumentOutOfRangeException>(() => writer.WriteString("é", 1));
    Throws<ArgumentOutOfRangeException>(() => writer.WriteBytes(new byte[] { 1, 2 }, 1));
    Throws<EncoderFallbackException>(() => writer.WriteString("\uD800"));
    Equal(0, writer.Length);
    writer.WriteString("é", 2);
    Equal(6, writer.Length);
    Throws<InvalidOperationException>(() => writer.WriteBoolean(true));
    Throws<InvalidOperationException>(() => writer.WriteBytes(Array.Empty<byte>()));
    Equal(6, writer.Length);
    using var tooSmall = new PayloadWriter(3);
    Throws<InvalidOperationException>(() => tooSmall.WriteString(""));
    Equal(0, tooSmall.Length);
    Throws<ArgumentOutOfRangeException>(() => writer.ToFrame((MessageKind)0));
    return Task.CompletedTask;
}

static byte[] Header(uint length, ushort version, MessageKind kind)
{
    var result = new byte[8];
    BinaryPrimitives.WriteUInt32LittleEndian(result, length);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), version);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), (ushort)kind);
    return result;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}.");

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

sealed class FragmentedReadStream : MemoryStream
{
    private readonly int _fragmentSize;
    public FragmentedReadStream(byte[] bytes, int fragmentSize) : base(bytes, writable: false) => _fragmentSize = fragmentSize;
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        base.ReadAsync(buffer, offset, Math.Min(count, _fragmentSize), cancellationToken);
}
