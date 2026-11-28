using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// T22: the speaker's own cat opens its mouth with his own voice (playtest 2026-11-20: Flox talked during an emote,
    /// his cat's mouth stayed shut on his screen), for as long as what he sent lasts, and shuts in a silence.
    /// </summary>
    [Category("Voice")]
    public class OwnVoiceMeterTests
    {
        private const int Rate = 24000;

        private static byte[] Tone(double seconds, double amplitude)
        {
            int _samples = (int)(Rate * seconds);
            var _pcm = new byte[_samples * 2];
            for (int i = 0; i < _samples; i++)
            {
                short _s = (short)(amplitude * 32767 * System.Math.Sin(2 * System.Math.PI * 200 * i / Rate));
                _pcm[2 * i] = (byte)(_s & 0xFF);
                _pcm[2 * i + 1] = (byte)((_s >> 8) & 0xFF);
            }
            return _pcm;
        }

        [Test]
        public void Voice_OpensTheMouth_ForAsLongAsItLasts()
        {
            var _meter = new OwnVoiceMeter(Rate);
            byte[] _packet = Tone(0.06, 0.3);
            _meter.Add(_packet, _packet.Length);

            Assert.Greater(_meter.Advance(0.01), 0.2f, "Mouth open while the packet plays.");
            Assert.Greater(_meter.Advance(0.03), 0.2f);
            Assert.AreEqual(0f, _meter.Advance(0.05), "Shut once the 60 ms are read.");
        }

        [Test]
        public void Silence_KeepsTheMouthShut_ThenTheNextPacketStartsAtOnce()
        {
            var _meter = new OwnVoiceMeter(Rate);
            Assert.AreEqual(0f, _meter.Advance(0.5), "Nothing sent: shut.");

            byte[] _quiet = Tone(0.06, 0.0);
            _meter.Add(_quiet, _quiet.Length);
            Assert.AreEqual(0f, _meter.Advance(0.02), "Silence in a packet: shut.");

            _meter.Advance(1.0); // a long pause
            byte[] _packet = Tone(0.06, 0.3);
            _meter.Add(_packet, _packet.Length);
            Assert.Greater(_meter.Advance(0.01), 0.2f, "After a pause the voice is shown at once, not a second late.");
        }
    }
}
