using System;
using System.IO;
using System.Text;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// NET-02 (epic-network-sync-hardening): what a client sends INSIDE its NGO connection request
    /// (<c>NetworkConfig.ConnectionData</c>). Carrying the profile in the request makes it atomic with approval: the
    /// server knows the joiner's pseudo before the joiner is even synchronized, with no follow-up round-trip that can
    /// be lost, reordered or answered twice. <see cref="BuildVersion"/> is read by the join gate (NET-05).
    /// Versioned, length-bounded binary format; any malformed input parses as "no payload", never throws.
    /// </summary>
    public sealed class ConnectionPayload
    {
        public const byte CurrentFormat = 2;
        // Format 1 (before rejoin) is still read: same fields, no rejoin token.
        private const byte Format1 = 1;
        private const int MaxStringBytes = 256;
        private const int MaxPayloadBytes = 2048;

        public string BuildVersion = string.Empty;
        public string PlayerName = string.Empty;
        public string PlayerFullName = string.Empty;
        public ulong SteamId;
        /// <summary>NET-05: the sender runs in the Unity Editor (version strings are meaningless there).</summary>
        public bool IsEditor;
        /// <summary>Rejoin 02: secret session token proving the sender owns a seat in the running game (empty on a
        /// first join). Checked by the host's connection approval when the game has already started.</summary>
        public string RejoinToken = string.Empty;

        public byte[] ToBytes()
        {
            using var _stream = new MemoryStream();
            using (var _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true))
            {
                _writer.Write(CurrentFormat);
                WriteString(_writer, BuildVersion);
                WriteString(_writer, PlayerName);
                WriteString(_writer, PlayerFullName);
                _writer.Write(SteamId);
                _writer.Write(IsEditor);
                WriteString(_writer, RejoinToken);
            }
            return _stream.ToArray();
        }

        public static bool TryParse(byte[] bytes, out ConnectionPayload payload)
        {
            payload = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxPayloadBytes)
            {
                return false;
            }

            try
            {
                using var _stream = new MemoryStream(bytes, writable: false);
                using var _reader = new BinaryReader(_stream, Encoding.UTF8);
                byte _format = _reader.ReadByte();
                if (_format != CurrentFormat && _format != Format1)
                {
                    return false;
                }

                var _result = new ConnectionPayload
                {
                    BuildVersion = ReadString(_reader),
                    PlayerName = ReadString(_reader),
                    PlayerFullName = ReadString(_reader),
                    SteamId = _reader.ReadUInt64(),
                    IsEditor = _reader.ReadBoolean(),
                };
                if (_format == CurrentFormat)
                {
                    _result.RejoinToken = ReadString(_reader);
                }
                payload = _result;
                return true;
            }
            catch (Exception)
            {
                return false; // truncated / garbage bytes → treated as a missing payload
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] _bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (_bytes.Length > MaxStringBytes)
            {
                Array.Resize(ref _bytes, MaxStringBytes);
            }
            writer.Write((ushort)_bytes.Length);
            writer.Write(_bytes);
        }

        private static string ReadString(BinaryReader reader)
        {
            int _length = reader.ReadUInt16();
            if (_length > MaxStringBytes)
            {
                throw new InvalidDataException("String too long.");
            }
            byte[] _bytes = reader.ReadBytes(_length);
            if (_bytes.Length != _length)
            {
                throw new EndOfStreamException();
            }
            return Encoding.UTF8.GetString(_bytes);
        }
    }
}
