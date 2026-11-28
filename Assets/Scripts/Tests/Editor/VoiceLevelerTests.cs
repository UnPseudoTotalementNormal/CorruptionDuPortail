using System;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// T22 voice levels (playtest 2026-11-14: « hyper saturé », then only the microphone's hiss once the volume was
    /// lowered): whatever level a microphone sends, the voice is played near dialogue level, never clipped, the noise
    /// between words is lowered, and the gain glides without clicks.
    /// </summary>
    [Category("Voice")]
    public class VoiceLevelerTests
    {
        private const int Rate = 48000;
        private const int Packet = Rate * 2 * 60 / 1000; // 60 ms of PCM16 mono, like a Steam packet

        // PCM16 mono sine of the given peak amplitude (0..1); clipped at full scale when above 1, like a hot microphone.
        private static byte[] Sine(double seconds, double amplitude, double hz = 220, int phase = 0)
        {
            int _n = (int)(seconds * Rate);
            var _pcm = new byte[_n * 2];
            for (int i = 0; i < _n; i++)
            {
                double _v = amplitude * Math.Sin(2 * Math.PI * hz * (i + phase) / Rate);
                int _s = (int)Math.Round(Math.Max(-1, Math.Min(1, _v)) * 32767);
                _pcm[2 * i] = (byte)(_s & 0xFF);
                _pcm[2 * i + 1] = (byte)((_s >> 8) & 0xFF);
            }
            return _pcm;
        }

        // Deterministic white noise of the given RMS.
        private static byte[] Noise(double seconds, double rms)
        {
            int _n = (int)(seconds * Rate);
            var _pcm = new byte[_n * 2];
            var _random = new Random(7);
            double _amplitude = rms * Math.Sqrt(3); // uniform -a..a has RMS a/√3
            for (int i = 0; i < _n; i++)
            {
                int _s = (int)Math.Round((_random.NextDouble() * 2 - 1) * _amplitude * 32767);
                _pcm[2 * i] = (byte)(_s & 0xFF);
                _pcm[2 * i + 1] = (byte)((_s >> 8) & 0xFF);
            }
            return _pcm;
        }

        private static double Sample(byte[] pcm, int i) => (short)(pcm[2 * i] | pcm[2 * i + 1] << 8) / 32768.0;

        // Fed packet by packet, like the network does.
        private static void Feed(VoiceLeveler leveler, byte[] pcm)
        {
            for (int _from = 0; _from < pcm.Length; _from += Packet)
            {
                int _count = Math.Min(Packet, pcm.Length - _from);
                var _packet = new byte[_count];
                Array.Copy(pcm, _from, _packet, 0, _count);
                leveler.Process(_packet, _count);
                Array.Copy(_packet, 0, pcm, _from, _count);
            }
        }

        private static double Rms(byte[] pcm, double fromSeconds, double toSeconds)
        {
            int _from = (int)(fromSeconds * Rate), _to = Math.Min(pcm.Length / 2, (int)(toSeconds * Rate));
            double _sum = 0;
            for (int i = _from; i < _to; i++)
            {
                _sum += Sample(pcm, i) * Sample(pcm, i);
            }
            return Math.Sqrt(_sum / (_to - _from));
        }

        private static double Peak(byte[] pcm)
        {
            double _peak = 0;
            for (int i = 0; i < pcm.Length / 2; i++)
            {
                _peak = Math.Max(_peak, Math.Abs(Sample(pcm, i)));
            }
            return _peak;
        }

        private static double Db(double linear) => 20 * Math.Log10(linear);

        [Test]
        public void ASaturatedMicrophone_ComesDownToDialogueLevel_AndNeverHitsFullScale()
        {
            var _leveler = new VoiceLeveler(Rate);
            byte[] _voice = Sine(1.0, 3.0); // driven 10 dB past full scale: a square-ish clipped wave

            Feed(_leveler, _voice);

            Assert.LessOrEqual(Peak(_voice), VoiceLeveler.Ceiling + 0.001);
            Assert.AreEqual(Db(VoiceLeveler.TargetRms), Db(Rms(_voice, 0.2, 1.0)), 3.0);
        }

        [Test]
        public void AQuietMicrophone_IsRaised_ButNoMoreThanMaxGain()
        {
            var _leveler = new VoiceLeveler(Rate);
            double _amplitude = 0.01 * Math.Sqrt(2); // RMS -40 dBFS
            byte[] _voice = Sine(1.0, _amplitude);

            Feed(_leveler, _voice);

            Assert.AreEqual(0.01 * VoiceLeveler.MaxGain, Rms(_voice, 0.2, 1.0), 0.001);
        }

        [Test]
        public void ANormalVoice_EndsNearTheTarget()
        {
            var _leveler = new VoiceLeveler(Rate);
            byte[] _voice = Sine(1.0, 0.15 * Math.Sqrt(2)); // RMS -16.5 dBFS

            Feed(_leveler, _voice);

            Assert.AreEqual(Db(VoiceLeveler.TargetRms), Db(Rms(_voice, 0.2, 1.0)), 1.0);
        }

        [Test]
        public void TheHissBetweenWords_IsLoweredMoreThanTheVoice()
        {
            var _leveler = new VoiceLeveler(Rate);
            double _voiceRms = 0.2, _hissRms = 0.008; // hiss 28 dB under the voice, above the absolute noise floor
            byte[] _voice = Sine(0.5, _voiceRms * Math.Sqrt(2));
            byte[] _hiss = Noise(0.5, _hissRms);

            Feed(_leveler, _voice);
            Feed(_leveler, _hiss);

            double _voiceGain = Rms(_voice, 0.3, 0.5) / _voiceRms;
            double _hissGain = Rms(_hiss, 0.1, 0.5) / _hissRms;
            Assert.Less(_hissGain, _voiceGain * VoiceLeveler.GateFloor * 1.5,
                $"voice gain {Db(_voiceGain):F1} dB, hiss gain {Db(_hissGain):F1} dB");
        }

        [Test]
        public void TheGainGlides_NoClickWhenAShoutFollowsSilence()
        {
            var _leveler = new VoiceLeveler(Rate);
            byte[] _quiet = Sine(0.3, 0.02);
            byte[] _shout = Sine(0.3, 1.0, phase: (int)(0.3 * Rate));

            Feed(_leveler, _quiet);
            Feed(_leveler, _shout);

            // A 220 Hz sine moves at most 2π·220/48000 ≈ 0.029 of its amplitude per sample (×2 while the gain still
            // carries the quiet voice's boost); dropping that gain in one step would jump by up to ~0.6.
            double _maxStep = 0;
            for (int i = 1; i < _shout.Length / 2; i++)
            {
                _maxStep = Math.Max(_maxStep, Math.Abs(Sample(_shout, i) - Sample(_shout, i - 1)));
            }
            Assert.Less(_maxStep, 0.1);
        }

        [Test]
        public void Burst_CountsWhatArrived_ClippedSamplesIncluded_AndResets()
        {
            var _leveler = new VoiceLeveler(Rate);
            Feed(_leveler, Sine(0.5, 3.0));

            Assert.AreEqual(Rate / 2, _leveler.Burst.Samples);
            Assert.Greater(_leveler.Burst.Clipped, Rate / 4, "a sine driven 10 dB past full scale is clipped most of the time");
            Assert.Greater(_leveler.Burst.Peak, 0.99);
            Assert.Less(_leveler.Burst.MeanGain, 0.2);

            _leveler.ResetBurst();
            Assert.AreEqual(0, _leveler.Burst.Samples);
            Assert.AreEqual(0, _leveler.Burst.Clipped);
        }

        // A voiced vowel the way a voice makes it: a glottal pulse 120 times a second, each ringing a 700 Hz formant that
        // dies in ~0.4 ms. Peaky like real speech (crest factor ~15 dB), scaled to the given RMS.
        private static byte[] Vowel(double seconds, double rms)
        {
            int _n = (int)(seconds * Rate);
            var _wave = new double[_n];
            double _sum = 0;
            for (int i = 0; i < _n; i++)
            {
                double _t = (i % (Rate / 120)) / (double)Rate; // time since the last pulse
                _wave[i] = Math.Exp(-_t / 0.0004) * Math.Sin(2 * Math.PI * 700 * _t);
                _sum += _wave[i] * _wave[i];
            }
            double _scale = rms / Math.Sqrt(_sum / _n);
            var _pcm = new byte[_n * 2];
            for (int i = 0; i < _n; i++)
            {
                int _s = (int)Math.Round(Math.Max(-1, Math.Min(1, _wave[i] * _scale)) * 32767);
                _pcm[2 * i] = (byte)(_s & 0xFF);
                _pcm[2 * i + 1] = (byte)((_s >> 8) & 0xFF);
            }
            return _pcm;
        }

        // How much of the output is not a scaled copy of the input (0 = same wave shape, only louder or quieter).
        private static double Distortion(byte[] input, byte[] output, double fromSeconds, double toSeconds)
        {
            // One scale per pitch period (1/120 s): a gain that moves (the level follower, the limiter) is not a
            // distortion, a wave whose shape changed within a period is.
            int _period = Rate / 120;
            double _residual = 0, _energy = 0;
            for (int _from = (int)(fromSeconds * Rate); _from + _period <= (int)(toSeconds * Rate); _from += _period)
            {
                double _io = 0, _ii = 0;
                for (int i = _from; i < _from + _period; i++)
                {
                    _io += Sample(input, i) * Sample(output, i);
                    _ii += Sample(input, i) * Sample(input, i);
                }
                double _k = _ii > 0 ? _io / _ii : 0;
                for (int i = _from; i < _from + _period; i++)
                {
                    double _r = Sample(output, i) - _k * Sample(input, i);
                    _residual += _r * _r;
                    _energy += Sample(output, i) * Sample(output, i);
                }
            }
            return Math.Sqrt(_residual / _energy);
        }

        // Playtest 2026-11-20 (« toujours saturé »): the old soft limiter bent every sample over -9 dBFS, so the peaks of
        // a voice already at dialogue level were reshaped (a saturation of its own). A voice must come out with the
        // same wave shape, only its level changed.
        [Test]
        public void AVoiceAtDialogueLevel_KeepsItsWaveShape()
        {
            byte[] _input = Vowel(1.5, VoiceLeveler.TargetRms);
            var _output = (byte[])_input.Clone();
            Feed(new VoiceLeveler(Rate), _output);

            Assert.Greater(Peak(_input), 0.35, "The test vowel must have speech-like peaks (over -9 dBFS).");
            Assert.Less(Distortion(_input, _output, 0.8, 1.4), 0.01, "The leveled voice must not be reshaped.");
        }

        // A shout far over the ceiling is turned down by the limiter's gain, not clipped: same wave, never past -3 dBFS.
        [Test]
        public void AShout_IsTurnedDownUnderTheCeiling_NotReshaped()
        {
            var _leveler = new VoiceLeveler(Rate);
            byte[] _calm = Vowel(1.0, VoiceLeveler.TargetRms);
            Feed(_leveler, _calm);
            byte[] _input = Vowel(0.3, 0.35); // +14 dB at once: the level follower needs a few blocks
            var _output = (byte[])_input.Clone();
            Feed(_leveler, _output);

            Assert.LessOrEqual(Peak(_output), VoiceLeveler.Ceiling + 0.001);
            Assert.Less(Distortion(_input, _output, 0.0, 0.1), 0.05, "The start of the shout, before the level follower caught up, keeps its shape.");
            Assert.Less(Distortion(_input, _output, 0.15, 0.29), 0.03, "Once the gain settled, the shout keeps its shape.");
        }

        // Playtest 2026-11-28: Wouh's voice arrived as hiss-like distortion at a sane level, nothing clipped, and the
        // level stats said all was well. The roughness tells a voice (energy in the lows) from noise (spread to the top).
        [Test]
        public void Roughness_TellsAVoiceFromNoise()
        {
            var _voice = new VoiceLeveler(Rate);
            Feed(_voice, Vowel(1.0, VoiceLeveler.TargetRms));
            var _noise = new VoiceLeveler(Rate);
            Feed(_noise, Noise(1.0, VoiceLeveler.TargetRms));

            Assert.Less(_voice.Burst.Roughness, 0.2, "A clean voice keeps its energy in the lows.");
            Assert.Greater(_noise.Burst.Roughness, 1.5, "White noise spreads it to the top (2 in theory).");
        }
    }
}
