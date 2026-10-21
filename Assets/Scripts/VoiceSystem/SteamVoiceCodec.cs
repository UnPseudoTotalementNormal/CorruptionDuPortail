#region

using System.IO;
using Steamworks;

#endregion

namespace VoiceSystem
{
    /// <summary>
    /// Steam Voice (Facepunch <see cref="SteamUser"/>): Steam opens the microphone, compresses the voice and decodes it
    /// back at this PC's optimal rate. Only the Steam build starts the Steam client (<c>SteamRuntime</c>): in the Relay
    /// build <see cref="IsAvailable"/> stays false and the voice is off.
    /// </summary>
    public sealed class SteamVoiceCodec : IVoiceCodec
    {
        private readonly MemoryStream _compressed = new();

        public bool IsAvailable => SteamClient.IsValid;

        public int SampleRate => (int)SteamUser.SampleRate;

        public bool Recording
        {
            get => SteamUser.VoiceRecord;
            set => SteamUser.VoiceRecord = value;
        }

        public int ReadCaptured(MemoryStream into) => SteamUser.HasVoiceData ? SteamUser.ReadVoiceData(into) : 0;

        public int Decode(byte[] compressed, int length, MemoryStream pcm)
        {
            _compressed.SetLength(0);
            _compressed.Write(compressed, 0, length);
            _compressed.Position = 0;
            pcm.SetLength(0);
            return SteamUser.DecompressVoice(_compressed, length, pcm);
        }
    }
}
