#region

using System.IO;

#endregion

namespace VoiceSystem
{
    /// <summary>
    /// Microphone capture + voice compression used by <see cref="VoiceChatManager"/>: Steam Voice in the game
    /// (<see cref="SteamVoiceCodec"/>), a fake in tests.
    /// </summary>
    public interface IVoiceCodec
    {
        /// <summary>Capture and decoding work on this PC (Steam running).</summary>
        bool IsAvailable { get; }

        /// <summary>Sample rate of the PCM16 mono audio <see cref="Decode"/> produces.</summary>
        int SampleRate { get; }

        /// <summary>Opens / closes the microphone.</summary>
        bool Recording { get; set; }

        /// <summary>Appends the compressed voice captured since the last call to <paramref name="into"/>. Returns the byte count.</summary>
        int ReadCaptured(MemoryStream into);

        /// <summary>Replaces <paramref name="pcm"/> with the PCM16 mono decoding of a received packet. Returns the byte count.</summary>
        int Decode(byte[] compressed, int length, MemoryStream pcm);
    }
}
