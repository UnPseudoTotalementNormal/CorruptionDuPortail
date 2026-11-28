using System;
using System.IO;
using NUnit.Framework;
using VoiceSystem;

namespace Tests.Editor
{
    /// <summary>
    /// The microphone picked in the options (mockups micro v1 C4, v2 O1 + V1): a PC microphone's voice, compressed by
    /// Opus, comes back out at the playback's rate with its loudness; « Ma voix » scales what the listeners play.
    /// </summary>
    [Category("Voice")]
    public class OpusMicCodecTests
    {
        // Interleaved PCM16 sine of the given amplitude (0..1), the same on every channel.
        private static short[] Sine(int rate, int channels, double seconds, double amplitude, double hz = 300)
        {
            int _n = (int)(rate * seconds);
            var _raw = new short[_n * channels];
            for (int i = 0; i < _n; i++)
            {
                short v = (short)(amplitude * 32767 * Math.Sin(2 * Math.PI * hz * i / rate));
                for (int c = 0; c < channels; c++)
                {
                    _raw[i * channels + c] = v;
                }
            }
            return _raw;
        }

        private static double Rms(MemoryStream pcm, int skipSamples)
        {
            byte[] b = pcm.ToArray();
            double s = 0;
            int n = 0;
            for (int i = skipSamples * 2; i + 1 < b.Length; i += 2)
            {
                double v = (short)(b[i] | (b[i + 1] << 8)) / 32768.0;
                s += v * v;
                n++;
            }
            return n == 0 ? 0 : Math.Sqrt(s / n);
        }

        [TestCase(48000, 1, 48000)]
        [TestCase(44100, 2, 44100)]
        [TestCase(16000, 1, 48000)]
        [TestCase(48000, 2, 24000)]
        public void Voice_RoundTrips_AtThePlaybackRate_WithItsLoudness(int deviceRate, int channels, int playbackRate)
        {
            var codec = new OpusMicCodec(() => "", playbackRate);
            codec.StartEncoderForTests();
            codec.Feed(Sine(deviceRate, channels, 0.16, 0.3), channels, deviceRate);

            var packet = new MemoryStream();
            int bytes = codec.EncodePending(packet);
            Assert.That(bytes, Is.GreaterThan(0).And.LessThan(VoiceChatManager.MaxPacketBytes), "one datagram");

            var pcm = new MemoryStream();
            int decoded = codec.Decode(packet.ToArray(), bytes, pcm);
            int frames = packet.ToArray()[0];
            int expectedSamples = (int)Math.Round(frames * 0.02 * playbackRate);
            Assert.That(decoded / 2, Is.InRange(expectedSamples - 2, expectedSamples + 2), "20 ms frames at the playback rate");
            // a 0.3 sine has an RMS of 0.21; Opus' start-up eats the first few ms
            Assert.That(Rms(pcm, playbackRate / 50), Is.InRange(0.15, 0.27));
        }

        [Test]
        public void ACorruptPacket_DecodesToNothing_WithoutThrowing()
        {
            var codec = new OpusMicCodec(() => "");
            var pcm = new MemoryStream();
            Assert.That(codec.Decode(new byte[] { 3, 200, 1, 2 }, 4, pcm), Is.EqualTo(0));
            Assert.That(codec.Decode(new byte[0], 0, pcm), Is.EqualTo(0));
        }

        [Test]
        public void MaVoix_ScalesTheVoice_AndNeverWrapsAround()
        {
            var pcm = new byte[] { 0x10, 0x27, 0xF0, 0xD8, 0xFF, 0x7F }; // 10000, -10000, 32767
            VoiceGain.Apply(pcm, pcm.Length, VoiceGain.FromByte(VoiceGain.ToByte(1.4f)));
            short a = (short)(pcm[0] | (pcm[1] << 8)), b = (short)(pcm[2] | (pcm[3] << 8)), c = (short)(pcm[4] | (pcm[5] << 8));
            Assert.That(a, Is.InRange(13900, 14100));
            Assert.That(b, Is.InRange(-14100, -13900));
            Assert.That(c, Is.EqualTo(short.MaxValue), "clamped, not wrapped to a negative sample");
        }

        [Test]
        public void MaVoix_AtItsDefault_LeavesTheVoiceAsItIs()
        {
            Assert.That(VoiceGain.FromByte(VoiceGain.ToByte(VoiceOptions.OwnVolumeUnity / VoiceOptions.OwnVolumeUnity)),
                Is.EqualTo(1f).Within(0.01f));
            Assert.That(VoiceGain.FromByte(VoiceGain.ToByte(1f / VoiceOptions.OwnVolumeUnity)), Is.EqualTo(1.43f).Within(0.02f),
                "the slider's top: +3 dB");
        }
    }
}
