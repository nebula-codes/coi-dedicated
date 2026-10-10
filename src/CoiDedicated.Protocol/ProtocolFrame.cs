using System;
using System.IO;

namespace CoiDedicated.Protocol
{
    public sealed class ProtocolException : IOException
    {
        public ProtocolException(string message) : base(message) { }
        public ProtocolException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>A known message kind and its bounded binary payload.</summary>
    public sealed class ProtocolFrame
    {
        public MessageKind Kind { get; }
        public ReadOnlyMemory<byte> Payload { get; }

        /// <summary>Copies the supplied payload; subsequent changes to the source cannot change this frame.</summary>
        public ProtocolFrame(MessageKind kind, ReadOnlySpan<byte> payload)
        {
            FrameCodec.ValidateKindArgument(kind);
            if (payload.Length > FrameCodec.MaxPayloadLength)
                throw new ArgumentOutOfRangeException(nameof(payload), "Payload exceeds the frame limit.");
            Kind = kind;
            Payload = payload.ToArray();
        }

        internal ProtocolFrame(MessageKind kind, byte[] ownedPayload, bool takeOwnership)
        {
            Kind = kind;
            Payload = ownedPayload;
        }
    }
}
