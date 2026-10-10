using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoiDedicated.Protocol
{
    /// <summary>
    /// Wire format: uint32 little-endian body length, uint16 version, uint16 kind, payload.
    /// Body length includes version and kind, excludes the length prefix, and is at most 1 MiB.
    /// Callers must serialize writes per stream and control cancellation, idle timeouts, and ownership.
    /// This codec does not provide authentication, encryption, compression, or session validation.
    /// </summary>
    public static class FrameCodec
    {
        public const ushort Version = 1;
        public const int MaxFrameBodyLength = 1024 * 1024;
        public const int BodyHeaderLength = 4;
        public const int MaxPayloadLength = MaxFrameBodyLength - BodyHeaderLength;

        /// <summary>
        /// Returns null only for EOF before any prefix byte. Mid-frame EOF throws EndOfStreamException.
        /// Rejects excessive lengths before allocating a payload and unknown headers before reading it.
        /// </summary>
        public static async Task<ProtocolFrame?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            cancellationToken.ThrowIfCancellationRequested();

            var prefix = new byte[4];
            int firstRead = await stream.ReadAsync(prefix, 0, prefix.Length, cancellationToken).ConfigureAwait(false);
            if (firstRead == 0) return null;
            await ReadExactlyAsync(stream, prefix, firstRead, prefix.Length - firstRead, cancellationToken).ConfigureAwait(false);

            uint bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
            if (bodyLength < BodyHeaderLength || bodyLength > MaxFrameBodyLength)
                throw new ProtocolException("Frame body length must be between 4 bytes and 1 MiB.");

            var header = new byte[BodyHeaderLength];
            await ReadExactlyAsync(stream, header, 0, header.Length, cancellationToken).ConfigureAwait(false);
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header);
            if (version != Version)
                throw new ProtocolException($"Unsupported protocol version {version}; expected {Version}.");
            var kind = (MessageKind)BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
            if (!IsKnownKind(kind))
                throw new ProtocolException($"Unknown message kind {(ushort)kind}.");

            var payload = new byte[(int)bodyLength - BodyHeaderLength];
            await ReadExactlyAsync(stream, payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
            return new ProtocolFrame(kind, payload, takeOwnership: true);
        }

        /// <summary>Writes one complete frame without flushing or closing the stream.</summary>
        public static async Task WriteAsync(Stream stream, ProtocolFrame frame, CancellationToken cancellationToken = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            cancellationToken.ThrowIfCancellationRequested();
            var header = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(frame.Payload.Length + BodyHeaderLength));
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), Version);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)frame.Kind);
            await stream.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(frame.Payload, cancellationToken).ConfigureAwait(false);
        }

        internal static bool IsKnownKind(MessageKind kind) => kind >= MessageKind.Hello && kind <= MessageKind.ResyncRequired;

        internal static void ValidateKindArgument(MessageKind kind)
        {
            if (!IsKnownKind(kind)) throw new ArgumentOutOfRangeException(nameof(kind), "Unknown message kind.");
        }

        private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            while (count > 0)
            {
                int read = await stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("The stream ended in the middle of a protocol frame.");
                offset += read;
                count -= read;
            }
        }
    }
}
