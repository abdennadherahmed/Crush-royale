using System;
using System.Collections.Generic;
using System.Text;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;

namespace CrushRoyale.Core.Replay
{
    public sealed class ReplayFormatException : Exception
    {
        public ReplayFormatException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Compact binary replay format (little-endian, LEB128 varints, CRC-32 footer).
    ///
    ///   "CRRP" | u8 formatVersion | var rulesVersion | u64 balanceHash | u8 mode | u64 seed | zigzag stageId
    ///   | string playerId | u8 highestLeague | var assistMoves | [v2: u8 pet | u8 petLevel]
    ///   | var loadoutCount { u8 type, var qty }
    ///   | var actionCount { u8 type, var deltaMs, payload, var scoreDelta }
    ///       swap payload:    u8 from(x&lt;&lt;4|y), u8 to
    ///       powerUp payload: u8 powerUp, u8 hasTarget, [u8 target]
    ///   | var checkpointCount { var deltaMs, u64 hash, var scoreDelta }
    ///   | u8 endState | var endTimeMs | var finalScore | u32 crc32
    /// Deltas are non-negative because time and score never go backwards within a replay.
    /// </summary>
    public static class ReplaySerializer
    {
        public const byte FormatVersion = 2;
        public const int MaxActions = 20000;
        public const int MaxCheckpoints = 2000;
        public const int MaxPlayerIdLength = 128;

        private static readonly byte[] Magic = { (byte)'C', (byte)'R', (byte)'R', (byte)'P' };

        public static byte[] Serialize(ReplayData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (data.Actions.Count > MaxActions || data.Checkpoints.Count > MaxCheckpoints)
            {
                throw new ReplayFormatException("Replay too large.");
            }

            var w = new ByteWriter();
            w.WriteBytes(Magic);
            w.WriteByte(FormatVersion);
            w.WriteVarUInt((ulong)data.RulesVersion);
            w.WriteUInt64(data.BalanceHash);
            w.WriteByte((byte)data.Mode);
            w.WriteUInt64(data.Seed);
            w.WriteVarInt(data.StageId);
            w.WriteString(data.PlayerId ?? string.Empty);
            w.WriteByte((byte)data.HighestLeague);
            w.WriteVarUInt((ulong)data.AssistExtraMoves);
            w.WriteByte((byte)data.Pet);
            w.WriteByte((byte)Math.Max(0, Math.Min(255, data.PetLevel)));

            w.WriteVarUInt((ulong)data.Loadout.Count);
            foreach (LoadoutEntry e in data.Loadout)
            {
                w.WriteByte((byte)e.Type);
                w.WriteVarUInt((ulong)e.Quantity);
            }

            int previousTime = 0;
            long previousScore = 0;
            w.WriteVarUInt((ulong)data.Actions.Count);
            foreach (ReplayAction ra in data.Actions)
            {
                PlayerAction a = ra.Action;
                if (a.TimestampMs < previousTime || ra.ScoreAfter < previousScore)
                {
                    throw new ReplayFormatException("Replay actions must be ordered by time and score.");
                }

                w.WriteByte((byte)a.Type);
                w.WriteVarUInt((ulong)(a.TimestampMs - previousTime));
                switch (a.Type)
                {
                    case ActionType.Swap:
                        w.WriteByte(PackPos(a.From));
                        w.WriteByte(PackPos(a.To));
                        break;
                    case ActionType.PowerUp:
                        w.WriteByte((byte)a.PowerUp);
                        w.WriteByte(a.HasTarget ? (byte)1 : (byte)0);
                        if (a.HasTarget)
                        {
                            w.WriteByte(PackPos(a.Target));
                        }
                        break;
                }
                w.WriteVarUInt((ulong)(ra.ScoreAfter - previousScore));
                previousTime = a.TimestampMs;
                previousScore = ra.ScoreAfter;
            }

            previousTime = 0;
            previousScore = 0;
            w.WriteVarUInt((ulong)data.Checkpoints.Count);
            foreach (ReplayCheckpoint c in data.Checkpoints)
            {
                if (c.TimeMs < previousTime || c.Score < previousScore)
                {
                    throw new ReplayFormatException("Checkpoints must be ordered.");
                }
                w.WriteVarUInt((ulong)(c.TimeMs - previousTime));
                w.WriteUInt64(c.BoardHash);
                w.WriteVarUInt((ulong)(c.Score - previousScore));
                previousTime = c.TimeMs;
                previousScore = c.Score;
            }

            w.WriteByte((byte)data.EndState);
            w.WriteVarUInt((ulong)Math.Max(0, data.EndTimeMs));
            w.WriteVarUInt((ulong)Math.Max(0, data.FinalScore));

            uint crc = Crc32.Compute(w.Buffer, 0, w.Length);
            w.WriteUInt32(crc);
            return w.ToArray();
        }

        /// <summary>Parses and fully validates a replay. Throws <see cref="ReplayFormatException"/> on any corruption.</summary>
        public static ReplayData Deserialize(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            if (bytes.Length < Magic.Length + 1 + 4)
            {
                throw new ReplayFormatException("Replay too short.");
            }

            uint expected = BitConverter.ToUInt32(bytes, bytes.Length - 4);
            if (!BitConverter.IsLittleEndian)
            {
                expected = ReverseBytes(expected);
            }
            if (Crc32.Compute(bytes, 0, bytes.Length - 4) != expected)
            {
                throw new ReplayFormatException("Checksum mismatch.");
            }

            var r = new ByteReader(bytes, bytes.Length - 4);
            for (int i = 0; i < Magic.Length; i++)
            {
                if (r.ReadByte() != Magic[i])
                {
                    throw new ReplayFormatException("Not a Crush Royale replay.");
                }
            }
            byte version = r.ReadByte();
            if (version < 1 || version > FormatVersion)
            {
                throw new ReplayFormatException("Unsupported replay format version " + version + ".");
            }

            var data = new ReplayData
            {
                RulesVersion = checked((int)r.ReadVarUInt()),
                BalanceHash = r.ReadUInt64(),
                Mode = ReadEnum<GameMode>(r.ReadByte()),
                Seed = r.ReadUInt64(),
                StageId = checked((int)r.ReadVarInt()),
                PlayerId = r.ReadString(MaxPlayerIdLength),
                HighestLeague = ReadEnum<League>(r.ReadByte()),
                AssistExtraMoves = checked((int)r.ReadVarUInt())
            };
            if (version >= 2)
            {
                data.Pet = ReadEnum<PetType>(r.ReadByte());
                data.PetLevel = r.ReadByte();
            }

            int loadoutCount = ReadCount(r, 9);
            for (int i = 0; i < loadoutCount; i++)
            {
                var type = ReadEnum<PowerUpType>(r.ReadByte());
                data.Loadout.Add(new LoadoutEntry(type, checked((int)r.ReadVarUInt())));
            }

            int actionCount = ReadCount(r, MaxActions);
            long time = 0;
            long score = 0;
            for (int i = 0; i < actionCount; i++)
            {
                var type = ReadEnum<ActionType>(r.ReadByte());
                time = checked(time + (long)r.ReadVarUInt());
                if (time > int.MaxValue)
                {
                    throw new ReplayFormatException("Timestamp overflow.");
                }

                PlayerAction action;
                switch (type)
                {
                    case ActionType.Swap:
                        action = PlayerAction.Swap(UnpackPos(r.ReadByte()), UnpackPos(r.ReadByte()), (int)time);
                        break;
                    case ActionType.PowerUp:
                        var powerUp = ReadEnum<PowerUpType>(r.ReadByte());
                        byte hasTarget = r.ReadByte();
                        if (hasTarget > 1)
                        {
                            throw new ReplayFormatException("Invalid target flag.");
                        }
                        action = hasTarget == 1
                            ? PlayerAction.UsePowerUp(powerUp, (int)time, UnpackPos(r.ReadByte()))
                            : PlayerAction.UsePowerUp(powerUp, (int)time);
                        break;
                    default:
                        action = PlayerAction.Continue((int)time);
                        break;
                }

                score = checked(score + (long)r.ReadVarUInt());
                data.Actions.Add(new ReplayAction(action, score));
            }

            int checkpointCount = ReadCount(r, MaxCheckpoints);
            time = 0;
            score = 0;
            for (int i = 0; i < checkpointCount; i++)
            {
                time = checked(time + (long)r.ReadVarUInt());
                ulong hash = r.ReadUInt64();
                score = checked(score + (long)r.ReadVarUInt());
                if (time > int.MaxValue)
                {
                    throw new ReplayFormatException("Checkpoint time overflow.");
                }
                data.Checkpoints.Add(new ReplayCheckpoint((int)time, hash, score));
            }

            data.EndState = ReadEnum<SessionState>(r.ReadByte());
            data.EndTimeMs = checked((int)r.ReadVarUInt());
            data.FinalScore = checked((long)r.ReadVarUInt());

            if (!r.AtEnd)
            {
                throw new ReplayFormatException("Trailing bytes in replay.");
            }
            return data;
        }

        public static OperationResult<ReplayData> TryDeserialize(byte[] bytes)
        {
            try
            {
                return OperationResult<ReplayData>.Ok(Deserialize(bytes));
            }
            catch (ReplayFormatException ex)
            {
                return OperationResult<ReplayData>.Fail(ErrorCode.ReplayInvalid, ex.Message);
            }
            catch (OverflowException ex)
            {
                return OperationResult<ReplayData>.Fail(ErrorCode.ReplayInvalid, ex.Message);
            }
            catch (ArgumentException ex)
            {
                return OperationResult<ReplayData>.Fail(ErrorCode.ReplayInvalid, ex.Message);
            }
        }

        private static byte PackPos(Pos p)
        {
            if (p.X < 0 || p.X > 15 || p.Y < 0 || p.Y > 15)
            {
                throw new ReplayFormatException("Position " + p + " cannot be packed.");
            }
            return (byte)((p.X << 4) | p.Y);
        }

        private static Pos UnpackPos(byte b) => new Pos(b >> 4, b & 0x0F);

        private static int ReadCount(ByteReader r, int max)
        {
            ulong count = r.ReadVarUInt();
            if (count > (ulong)max)
            {
                throw new ReplayFormatException("Count " + count + " exceeds limit " + max + ".");
            }
            return (int)count;
        }

        private static T ReadEnum<T>(byte value) where T : struct, Enum
        {
            object boxed = Enum.ToObject(typeof(T), value);
            if (!Enum.IsDefined(typeof(T), boxed))
            {
                throw new ReplayFormatException("Invalid " + typeof(T).Name + " value " + value + ".");
            }
            return (T)boxed;
        }

        private static uint ReverseBytes(uint v) =>
            (v & 0x000000FFU) << 24 | (v & 0x0000FF00U) << 8 | (v & 0x00FF0000U) >> 8 | (v & 0xFF000000U) >> 24;

        private sealed class ByteWriter
        {
            private byte[] _buffer = new byte[256];

            public int Length { get; private set; }

            public byte[] Buffer => _buffer;

            public void WriteByte(byte value)
            {
                Ensure(1);
                _buffer[Length++] = value;
            }

            public void WriteBytes(byte[] values)
            {
                foreach (byte b in values)
                {
                    WriteByte(b);
                }
            }

            public void WriteVarUInt(ulong value)
            {
                while (value >= 0x80)
                {
                    WriteByte((byte)(value | 0x80));
                    value >>= 7;
                }
                WriteByte((byte)value);
            }

            public void WriteVarInt(long value) => WriteVarUInt((ulong)((value << 1) ^ (value >> 63)));

            public void WriteUInt32(uint value)
            {
                for (int i = 0; i < 4; i++)
                {
                    WriteByte((byte)(value >> (8 * i)));
                }
            }

            public void WriteUInt64(ulong value)
            {
                for (int i = 0; i < 8; i++)
                {
                    WriteByte((byte)(value >> (8 * i)));
                }
            }

            public void WriteString(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                if (value.Length > MaxPlayerIdLength)
                {
                    throw new ReplayFormatException("String too long.");
                }
                WriteVarUInt((ulong)bytes.Length);
                WriteBytes(bytes);
            }

            public byte[] ToArray()
            {
                var result = new byte[Length];
                Array.Copy(_buffer, result, Length);
                return result;
            }

            private void Ensure(int extra)
            {
                if (Length + extra <= _buffer.Length)
                {
                    return;
                }
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + extra));
            }
        }

        private sealed class ByteReader
        {
            private readonly byte[] _bytes;
            private readonly int _end;
            private int _position;

            public ByteReader(byte[] bytes, int end)
            {
                _bytes = bytes;
                _end = end;
            }

            public bool AtEnd => _position == _end;

            public byte ReadByte()
            {
                if (_position >= _end)
                {
                    throw new ReplayFormatException("Unexpected end of replay.");
                }
                return _bytes[_position++];
            }

            public ulong ReadVarUInt()
            {
                ulong result = 0;
                int shift = 0;
                while (true)
                {
                    if (shift > 63)
                    {
                        throw new ReplayFormatException("Varint too long.");
                    }
                    byte b = ReadByte();
                    result |= (ulong)(b & 0x7F) << shift;
                    if ((b & 0x80) == 0)
                    {
                        return result;
                    }
                    shift += 7;
                }
            }

            public long ReadVarInt()
            {
                ulong raw = ReadVarUInt();
                return (long)(raw >> 1) ^ -(long)(raw & 1);
            }

            public ulong ReadUInt64()
            {
                ulong value = 0;
                for (int i = 0; i < 8; i++)
                {
                    value |= (ulong)ReadByte() << (8 * i);
                }
                return value;
            }

            public string ReadString(int maxChars)
            {
                ulong length = ReadVarUInt();
                if (length > (ulong)(maxChars * 4))
                {
                    throw new ReplayFormatException("String too long.");
                }
                var bytes = new byte[(int)length];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = ReadByte();
                }
                return Encoding.UTF8.GetString(bytes);
            }
        }
    }

    /// <summary>Standard CRC-32 (IEEE 802.3, reflected, polynomial 0xEDB88320).</summary>
    public static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFU;
            for (int i = offset; i < offset + count; i++)
            {
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }
            return ~crc;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320U ^ (c >> 1) : c >> 1;
                }
                table[i] = c;
            }
            return table;
        }
    }
}
