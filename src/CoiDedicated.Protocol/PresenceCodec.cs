using System;
using System.Collections.Generic;

namespace CoiDedicated.Protocol
{
    // Presence is visual advice only. None of these values are simulation inputs.
    public readonly struct PresencePoint
    {
        public readonly float X, Y, Z;
        public PresencePoint(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    public sealed class PresencePreview
    {
        public string Prototype { get; set; } = "";
        public PresencePoint[] Corners { get; set; } = Array.Empty<PresencePoint>();
    }

    public sealed class PlayerPresenceState
    {
        public bool Visible { get; set; }
        public PresencePoint Center { get; set; }
        public PresencePoint[] ViewCorners { get; set; } = Array.Empty<PresencePoint>();
        public PresencePreview[] Previews { get; set; } = Array.Empty<PresencePreview>();
        public bool PreviewsTruncated { get; set; }
    }

    public sealed class PlayerPresenceEntry
    {
        public uint PeerId { get; set; }
        public string Name { get; set; } = "";
        public PlayerPresenceState State { get; set; } = new PlayerPresenceState();
    }

    /// <summary>Bounded, versioned display state; updates carry no player identity.</summary>
    public static class PresenceCodec
    {
        public const int MaxPeers = 8;
        public const int MaxPreviews = 32;
        public const int MaxUpdateBytes = 8192;
        public const int MaxSnapshotBytes = 65536;
        public const int IntervalMilliseconds = 200;
        public const int ExpiryMilliseconds = 3000;
        public const float MaxCoordinate = 1000000;

        public static ProtocolFrame EncodeUpdate(PlayerPresenceState state)
        {
            using var writer = new PayloadWriter(MaxUpdateBytes);
            writer.WriteUInt16(1);
            WriteState(writer, state);
            return writer.ToFrame(MessageKind.PresenceUpdate);
        }

        public static PlayerPresenceState DecodeUpdate(ReadOnlyMemory<byte> payload)
        {
            var reader = Reader(payload, MaxUpdateBytes);
            var result = ReadState(reader);
            reader.EnsureEnd();
            return result;
        }

        public static ProtocolFrame EncodeSnapshot(IReadOnlyList<PlayerPresenceEntry> entries)
        {
            if (entries == null || entries.Count > MaxPeers) throw new ProtocolException("Too many presence peers.");
            using var writer = new PayloadWriter(MaxSnapshotBytes);
            writer.WriteUInt16(1);
            writer.WriteUInt16((ushort)entries.Count);
            var ids = new HashSet<uint>();
            foreach (var entry in entries)
            {
                if (entry.PeerId == 0 || !ids.Add(entry.PeerId)) throw new ProtocolException("Invalid presence identity.");
                ValidateName(entry.Name);
                writer.WriteUInt32(entry.PeerId);
                writer.WriteString(entry.Name, 64);
                WriteState(writer, entry.State);
            }
            return writer.ToFrame(MessageKind.PresenceSnapshot);
        }

        public static PlayerPresenceEntry[] DecodeSnapshot(ReadOnlyMemory<byte> payload)
        {
            var reader = Reader(payload, MaxSnapshotBytes);
            int count = reader.ReadUInt16();
            if (count > MaxPeers) throw new ProtocolException("Too many presence peers.");
            var result = new PlayerPresenceEntry[count];
            var ids = new HashSet<uint>();
            for (int i = 0; i < count; i++)
            {
                uint id = reader.ReadUInt32();
                if (id == 0 || !ids.Add(id)) throw new ProtocolException("Invalid presence identity.");
                string name = reader.ReadString(64);
                ValidateName(name);
                result[i] = new PlayerPresenceEntry { PeerId = id, Name = name, State = ReadState(reader) };
            }
            reader.EnsureEnd();
            return result;
        }

        private static PayloadReader Reader(ReadOnlyMemory<byte> payload, int limit)
        {
            if (payload.Length > limit) throw new ProtocolException("Presence payload exceeds its byte limit.");
            var reader = new PayloadReader(payload);
            if (reader.ReadUInt16() != 1) throw new ProtocolException("Unknown presence version.");
            return reader;
        }

        private static void WriteState(PayloadWriter writer, PlayerPresenceState state)
        {
            if (state == null) throw new ProtocolException("Missing presence state.");
            writer.WriteBoolean(state.Visible);
            if (!state.Visible) return;
            WritePoint(writer, state.Center);
            WriteQuad(writer, state.ViewCorners, optional: true);
            if (state.Previews == null || state.Previews.Length > MaxPreviews) throw new ProtocolException("Too many building previews.");
            writer.WriteBoolean(state.PreviewsTruncated);
            writer.WriteUInt16((ushort)state.Previews.Length);
            foreach (var preview in state.Previews)
            {
                ValidateLabel(preview.Prototype);
                writer.WriteString(preview.Prototype, 96);
                WriteQuad(writer, preview.Corners, optional: false);
            }
        }

        private static PlayerPresenceState ReadState(PayloadReader reader)
        {
            var state = new PlayerPresenceState { Visible = reader.ReadBoolean() };
            if (!state.Visible) return state;
            state.Center = ReadPoint(reader);
            state.ViewCorners = ReadQuad(reader, optional: true);
            state.PreviewsTruncated = reader.ReadBoolean();
            int count = reader.ReadUInt16();
            if (count > MaxPreviews) throw new ProtocolException("Too many building previews.");
            state.Previews = new PresencePreview[count];
            for (int i = 0; i < count; i++)
            {
                string label = reader.ReadString(96);
                ValidateLabel(label);
                state.Previews[i] = new PresencePreview { Prototype = label, Corners = ReadQuad(reader, optional: false) };
            }
            return state;
        }

        private static void WriteQuad(PayloadWriter writer, PresencePoint[] points, bool optional)
        {
            if (points == null || (points.Length != 4 && !(optional && points.Length == 0))) throw new ProtocolException("Invalid presence outline.");
            writer.WriteBoolean(points.Length != 0);
            foreach (var point in points) WritePoint(writer, point);
        }

        private static PresencePoint[] ReadQuad(PayloadReader reader, bool optional)
        {
            if (!reader.ReadBoolean())
            {
                if (!optional) throw new ProtocolException("Missing preview outline.");
                return Array.Empty<PresencePoint>();
            }
            return new[] { ReadPoint(reader), ReadPoint(reader), ReadPoint(reader), ReadPoint(reader) };
        }

        private static void WritePoint(PayloadWriter writer, PresencePoint point)
        {
            ValidateCoordinate(point.X); ValidateCoordinate(point.Y); ValidateCoordinate(point.Z);
            writer.WriteInt32(BitConverter.SingleToInt32Bits(point.X));
            writer.WriteInt32(BitConverter.SingleToInt32Bits(point.Y));
            writer.WriteInt32(BitConverter.SingleToInt32Bits(point.Z));
        }

        private static PresencePoint ReadPoint(PayloadReader reader)
        {
            float x = BitConverter.Int32BitsToSingle(reader.ReadInt32());
            float y = BitConverter.Int32BitsToSingle(reader.ReadInt32());
            float z = BitConverter.Int32BitsToSingle(reader.ReadInt32());
            ValidateCoordinate(x); ValidateCoordinate(y); ValidateCoordinate(z);
            return new PresencePoint(x, y, z);
        }

        private static void ValidateCoordinate(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > MaxCoordinate)
                throw new ProtocolException("Presence coordinate is not finite or in bounds.");
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64 || name == "server") throw new ProtocolException("Invalid presence name.");
            foreach (char c in name)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-')
                    throw new ProtocolException("Invalid presence name.");
        }

        private static void ValidateLabel(string label)
        {
            if (string.IsNullOrEmpty(label) || label.Length > 96) throw new ProtocolException("Invalid preview label.");
            foreach (char c in label)
                if (c < 32 || c > 126 || c == '<' || c == '>') throw new ProtocolException("Invalid preview label.");
        }
    }
}
