using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// T22 talking mouth: the voice level is read where the mixer plays (absolute ring bytes), not when the packet came,
    /// a gap reads as silence, and the mouth's openness follows the level in decibels with a small fidget on top.
    /// </summary>
    [Category("Voice")]
    public class VoiceLevelTrackTests
    {
        private const int Block = 100;

        // PCM16 mono, every sample at the same amplitude (0..1 of full scale).
        private static byte[] Pcm(int bytes, double amplitude)
        {
            var _pcm = new byte[bytes];
            short _s = (short)(amplitude * 32767);
            for (int i = 0; i + 1 < bytes; i += 2)
            {
                _s = (short)-_s; // a square wave: same RMS, not a DC offset
                _pcm[i] = (byte)(_s & 0xFF);
                _pcm[i + 1] = (byte)((_s >> 8) & 0xFF);
            }
            return _pcm;
        }

        [Test]
        public void Level_IsTheRmsOfTheBlockUnderTheReader()
        {
            var _track = new VoiceLevelTrack(Block);
            _track.Add(1000, Pcm(200, 0.5), 200);

            Assert.AreEqual(0.5, _track.LevelAt(1000), 0.001);
            Assert.AreEqual(0.5, _track.LevelAt(1199), 0.001);
        }

        [Test]
        public void BeforeTheVoiceArrives_AndAfterItPlayed_ItIsSilent()
        {
            var _track = new VoiceLevelTrack(Block);
            _track.Add(1000, Pcm(200, 0.5), 200);

            Assert.AreEqual(0, _track.LevelAt(900), "the prebuffer ahead of the voice is silence");
            Assert.AreEqual(0, _track.LevelAt(1200), "past the last block");
        }

        [Test]
        public void Level_FollowsEachBlock_LoudThenQuiet()
        {
            var _track = new VoiceLevelTrack(Block);
            _track.Add(0, Pcm(100, 0.8), 100);
            _track.Add(100, Pcm(100, 0.1), 100);

            Assert.AreEqual(0.8, _track.LevelAt(50), 0.001);
            Assert.AreEqual(0.1, _track.LevelAt(150), 0.001);
        }

        [Test]
        public void Openness_ShutsOnRoomNoise_OpensFullyOnALoudVoice()
        {
            Assert.AreEqual(0, VoiceLevelTrack.Openness(0));
            Assert.AreEqual(0, VoiceLevelTrack.Openness(0.003), "≈ -50 dB: room noise");
            Assert.AreEqual(1, VoiceLevelTrack.Openness(0.5), "≈ -6 dB: shouting");
            double _talk = VoiceLevelTrack.Openness(0.05); // ≈ -26 dB: a normal voice
            Assert.That(_talk, Is.GreaterThan(0.3).And.LessThan(0.8));
        }

        [Test]
        public void MouthScale_IsShutWhenSilent_AndWobblesWithinTheFidgetShare()
        {
            Assert.AreEqual(0, VoiceLevelTrack.MouthScale(0, 1.23, 0.25));
            for (double t = 0; t < 2; t += 0.013)
            {
                double _scale = VoiceLevelTrack.MouthScale(0.8, t, 0.25);
                Assert.That(_scale, Is.InRange(0.8 * 0.75 - 1e-9, 0.8 + 1e-9));
            }
            Assert.AreEqual(0.8, VoiceLevelTrack.MouthScale(0.8, 0.7, 0), 1e-9, "no fidget: the voice alone");
        }
    }
}
