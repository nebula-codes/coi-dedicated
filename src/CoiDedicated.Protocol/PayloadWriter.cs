using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace CoiDedicated.Protocol
{
    /// <summary>Explicit little-endian primitives; strings are strict UTF-8 with uint32 byte lengths.</summary>
    public sealed class PayloadWriter : IDisposable
    {
        public const int DefaultMaxStringByteLength = 4096;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly MemoryStream _stream = new MemoryStream();
        private readonly int _maxLength;

        public PayloadWriter(int maxLength = FrameCodec.MaxPayloadLength)
        {
            if (maxLength < 0 || maxLength > FrameCodec.MaxPayloadLength)
                throw new ArgumentOutOfRangeException(nameof(maxLength));
            _maxLength = maxLength;
        }

        public int Length => checked((int)_stream.Length);

        public void WriteBoolean(bool value)
        {
            EnsureSpace(1);
            _stream.WriteByte(value ? (byte)1 : (byte)0);
        }

        public void WriteUInt16(ushort value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            WriteRaw(buffer);
        }

        public void WriteUInt32(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            WriteRaw(buffer);
        }

        public void WriteUInt64(ulong value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
            WriteRaw(buffer);
        }

        public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));
        public void WriteInt64(long value) => WriteUInt64(unchecked((ulong)value));

        public void WriteString(string value, int maxByteLength = DefaultMaxStringByteLength)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ValidateLimit(maxByteLength);
            int byteLength = Utf8.GetByteCount(value);
            if (byteLength > maxByteLength)
                throw new ArgumentOutOfRangeException(nameof(value), "Encoded string exceeds its byte limit.");
            EnsureSpaceWithPrefix(byteLength);
            var bytes = Utf8.GetBytes(value);
            WriteUInt32((uint)byteLength);
            WriteRaw(bytes);
        }

        public void WriteBytes(ReadOnlySpan<byte> value, int maxByteLength = FrameCodec.MaxPayloadLength)
        {
            ValidateLimit(maxByteLength);
            if (value.Length > maxByteLength)
                throw new ArgumentOutOfRangeException(nameof(value), "Byte array exceeds its length limit.");
            EnsureSpaceWithPrefix(value.Length);
            WriteUInt32((uint)value.Length);
            WriteRaw(value);
        }

        public byte[] ToArray() => _stream.ToArray();

        public ProtocolFrame ToFrame(MessageKind kind)
        {
            FrameCodec.ValidateKindArgument(kind);
            return new ProtocolFrame(kind, ToArray(), takeOwnership: true);
        }

        public void Dispose() => _stream.Dispose();

        private void WriteRaw(ReadOnlySpan<byte> bytes)
        {
            EnsureSpace(bytes.Length);
            _stream.Write(bytes);
        }

        private void EnsureSpaceWithPrefix(int count)
        {
            if (count > _maxLength - 4 || Length > _maxLength - 4 - count)
                throw new InvalidOperationException("Payload exceeds its configured byte limit.");
        }

        private void EnsureSpace(int count)
        {
            if (count > _maxLength - Length)
                throw new InvalidOperationException("Payload exceeds its configured byte limit.");
        }

        private static void ValidateLimit(int limit)
        {
            if (limit < 0 || limit > FrameCodec.MaxPayloadLength)
                throw new ArgumentOutOfRangeException(nameof(limit));
        }
    }
}
