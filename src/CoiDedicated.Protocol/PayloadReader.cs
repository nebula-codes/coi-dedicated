using System;
using System.Buffers.Binary;
using System.Text;

namespace CoiDedicated.Protocol
{
    /// <summary>Reads explicitly typed fields from one payload, rejecting malformed sizes and encodings.</summary>
    public sealed class PayloadReader
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly ReadOnlyMemory<byte> _payload;
        private int _position;

        public PayloadReader(ReadOnlyMemory<byte> payload)
        {
            if (payload.Length > FrameCodec.MaxPayloadLength)
                throw new ArgumentOutOfRangeException(nameof(payload));
            _payload = payload;
        }

        public int Remaining => _payload.Length - _position;
        public int Position => _position;

        public bool ReadBoolean()
        {
            byte value = Take(1)[0];
            if (value > 1) throw new ProtocolException("Boolean must be encoded as 0 or 1.");
            return value == 1;
        }

        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
        public int ReadInt32() => unchecked((int)ReadUInt32());
        public long ReadInt64() => unchecked((long)ReadUInt64());

        public string ReadString(int maxByteLength = PayloadWriter.DefaultMaxStringByteLength)
        {
            int count = ReadBoundedLength(maxByteLength);
            try { return Utf8.GetString(Take(count)); }
            catch (DecoderFallbackException ex) { throw new ProtocolException("String contains invalid UTF-8.", ex); }
        }

        public byte[] ReadBytes(int maxByteLength = FrameCodec.MaxPayloadLength)
        {
            int count = ReadBoundedLength(maxByteLength);
            return Take(count).ToArray();
        }

        /// <summary>Call after the last expected field to reject unexpected or incompatible trailing data.</summary>
        public void EnsureEnd()
        {
            if (Remaining != 0) throw new ProtocolException("Unexpected trailing payload bytes.");
        }

        private int ReadBoundedLength(int limit)
        {
            if (limit < 0 || limit > FrameCodec.MaxPayloadLength)
                throw new ArgumentOutOfRangeException(nameof(limit));
            uint count = ReadUInt32();
            if (count > (uint)limit) throw new ProtocolException("Field length exceeds its configured byte limit.");
            if (count > (uint)Remaining) throw new ProtocolException("Field length exceeds the remaining payload.");
            return (int)count;
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count > Remaining) throw new ProtocolException("Payload ended before the expected field.");
            var result = _payload.Span.Slice(_position, count);
            _position += count;
            return result;
        }
    }
}
