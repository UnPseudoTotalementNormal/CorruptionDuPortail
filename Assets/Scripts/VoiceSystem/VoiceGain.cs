#region

using System;

#endregion

namespace VoiceSystem
{
    /// <summary>Which compression made a voice packet: the listener decodes it with the same one.</summary>
    public enum VoiceCodecId : byte
    {
        /// <summary>Steam Voice, Steam's own microphone (<see cref="SteamVoiceCodec"/>).</summary>
        Steam = 0,
        /// <summary>A microphone of this PC picked in the options (<see cref="OpusMicCodec"/>).</summary>
        Opus = 1,
    }

    /// <summary>
    /// « Ma voix » (mockups micro v2, V1 remark): how loud the others hear a player. Sent as one byte with each packet
    /// and applied by each listener after its leveler, which would otherwise bring every voice back to the same level.
    /// </summary>
    public static class VoiceGain
    {
        /// <summary>The highest gain a byte carries (255).</summary>
        public const float Max = 2f;

        public static byte ToByte(float _gain) => (byte)Math.Round(Math.Clamp(_gain, 0f, Max) / Max * 255f);

        public static float FromByte(byte _value) => _value / 255f * Max;

        /// <summary>Scales PCM16 mono bytes 0..<paramref name="_count"/> in place, clamped to full scale.</summary>
        public static void Apply(byte[] _pcm, int _count, float _gain)
        {
            if (Math.Abs(_gain - 1f) < 0.01f)
            {
                return;
            }
            for (int i = 0; i + 1 < _count; i += 2)
            {
                int _sample = (short)(_pcm[i] | (_pcm[i + 1] << 8));
                int _scaled = (int)Math.Round(_sample * _gain);
                _scaled = Math.Clamp(_scaled, short.MinValue, short.MaxValue);
                _pcm[i] = (byte)(_scaled & 0xFF);
                _pcm[i + 1] = (byte)((_scaled >> 8) & 0xFF);
            }
        }
    }
}
