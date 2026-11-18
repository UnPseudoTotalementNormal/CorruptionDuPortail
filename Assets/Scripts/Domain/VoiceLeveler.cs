using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Evens out one remote player's voice before it is played (T22). Steam hands out the voice at whatever level the
    /// speaker's microphone, Windows and Steam's own gain control give it: playtest 2026-11-14, Wouh and Flox heard each
    /// other « hyper saturé », and lowering the volume left only the microphone's hiss. Each decoded PCM16 mono packet is
    /// processed in place, in 10 ms blocks:
    /// <list type="bullet">
    /// <item>gain: the speech level is followed (fast down, slow up) and brought to <see cref="TargetRms"/>, between
    /// <see cref="MinGain"/> and <see cref="MaxGain"/>, so a hot microphone comes down and a quiet one comes up a little;</item>
    /// <item>gate: a block far below the speech level (or under <see cref="NoiseFloorRms"/>) is room noise, the space
    /// between words: it is lowered to <see cref="GateFloor"/>;</item>
    /// <item>limiter: whatever is left above the knee bends softly under <see cref="Ceiling"/>, never a hard clip.</item>
    /// </list>
    /// Gains glide sample by sample inside a block, so nothing clicks. <see cref="Burst"/> measures what came in (for
    /// the playtest logs: was it already clipped when it arrived?). Pure: no Unity, no FMOD.
    /// </summary>
    public sealed class VoiceLeveler
    {
        /// <summary>Speech is brought to this RMS (≈ -23 dBFS, dialogue level).</summary>
        public const double TargetRms = 0.07;
        /// <summary>A quiet microphone is raised at most this much (+6 dB): enough for a voice, not for its noise.</summary>
        public const double MaxGain = 2.0;
        /// <summary>A hot microphone is lowered at most this much (-26 dB).</summary>
        public const double MinGain = 0.05;
        /// <summary>Under this RMS (≈ -46 dBFS) a block is never speech.</summary>
        public const double NoiseFloorRms = 0.005;
        /// <summary>A block this far under the speech level (-24 dB) is the noise between words.</summary>
        public const double GateRatio = 0.063;
        /// <summary>What the gate leaves of the noise (-20 dB): quieter, not a hole.</summary>
        public const double GateFloor = 0.1;
        /// <summary>The limiter starts bending here (≈ -9 dBFS)...</summary>
        public const double Knee = 0.35;
        /// <summary>...and never goes past this (≈ -3 dBFS).</summary>
        public const double Ceiling = 0.7;

        private const double BlockSeconds = 0.01;
        // Per 10 ms block: the level follower rises fast (a louder voice is lowered within ~30 ms) and falls slowly
        // (~0.5 s), so the gain does not pump between syllables.
        private const double LevelAttack = 0.3;
        private const double LevelRelease = 0.02;
        // The gate opens at once and closes over ~50 ms.
        private const double GateClose = 0.2;
        private const short ClippedSample = 32000;

        private readonly int _blockSamples;
        private double _speechLevel;
        private double _gain = 1.0;
        private double _gate = 1.0;

        /// <summary>What came in since the last <see cref="ResetBurst"/>, before any processing.</summary>
        public BurstStats Burst;

        public struct BurstStats
        {
            /// <summary>Samples received.</summary>
            public int Samples;
            /// <summary>Loudest sample, 0..1 of full scale.</summary>
            public double Peak;
            /// <summary>Samples at full scale already: the voice was clipped before it was sent.</summary>
            public int Clipped;
            /// <summary>Sum of squares of the speech blocks (for their RMS).</summary>
            public double SpeechSquares;
            public int SpeechSamples;
            /// <summary>Sum of the gain applied to each speech block, and their count.</summary>
            public double GainSum;
            public int GainBlocks;

            public double SpeechRms => SpeechSamples > 0 ? Math.Sqrt(SpeechSquares / SpeechSamples) : 0;
            public double MeanGain => GainBlocks > 0 ? GainSum / GainBlocks : 1;
        }

        public VoiceLeveler(int sampleRate)
        {
            _blockSamples = Math.Max(1, (int)(sampleRate * BlockSeconds));
        }

        public void ResetBurst() => Burst = default;

        /// <summary>Levels <paramref name="count"/> bytes of PCM16 mono little-endian in place.</summary>
        public void Process(byte[] pcm, int count)
        {
            int _samples = count / 2;
            for (int _from = 0; _from < _samples; _from += _blockSamples)
            {
                int _to = Math.Min(_samples, _from + _blockSamples);
                ProcessBlock(pcm, _from, _to);
            }
        }

        private void ProcessBlock(byte[] pcm, int from, int to)
        {
            double _sum = 0;
            for (int i = from; i < to; i++)
            {
                double _s = Read(pcm, i);
                double _abs = Math.Abs(_s);
                _sum += _s * _s;
                if (_abs > Burst.Peak)
                {
                    Burst.Peak = _abs;
                }
                if (_abs * 32768.0 >= ClippedSample)
                {
                    Burst.Clipped++;
                }
            }
            int _n = to - from;
            Burst.Samples += _n;
            double _rms = Math.Sqrt(_sum / _n);

            bool _speech = _rms >= NoiseFloorRms && _rms >= _speechLevel * GateRatio;
            if (_speech)
            {
                _speechLevel = _speechLevel <= 0
                    ? _rms
                    : _speechLevel + (_rms - _speechLevel) * (_rms > _speechLevel ? LevelAttack : LevelRelease);
                Burst.SpeechSquares += _sum;
                Burst.SpeechSamples += _n;
            }
            double _targetGain = _speechLevel > 0 ? Clamp(TargetRms / _speechLevel, MinGain, MaxGain) : 1.0;
            double _targetGate = _speech ? 1.0 : Math.Max(GateFloor, _gate + (GateFloor - _gate) * GateClose);
            if (_speech)
            {
                Burst.GainSum += _targetGain;
                Burst.GainBlocks++;
            }

            // Glide from the previous block's gains to these ones across the block: no step, no click.
            double _startGain = _gain * _gate;
            double _endGain = _targetGain * _targetGate;
            for (int i = from; i < to; i++)
            {
                double _t = (i - from + 1) / (double)_n;
                double _g = _startGain + (_endGain - _startGain) * _t;
                Write(pcm, i, Limit(Read(pcm, i) * _g));
            }
            _gain = _targetGain;
            _gate = _targetGate;
        }

        /// <summary>Linear under <see cref="Knee"/>, then a soft bend that tends to <see cref="Ceiling"/>.</summary>
        public static double Limit(double x)
        {
            double _abs = Math.Abs(x);
            if (_abs <= Knee)
            {
                return x;
            }
            double _room = Ceiling - Knee;
            double _bent = Knee + _room * Math.Tanh((_abs - Knee) / _room);
            return x < 0 ? -_bent : _bent;
        }

        private static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;

        private static double Read(byte[] pcm, int sample) =>
            (short)(pcm[2 * sample] | pcm[2 * sample + 1] << 8) / 32768.0;

        private static void Write(byte[] pcm, int sample, double value)
        {
            int _v = (int)Math.Round(value * 32767.0);
            _v = _v > short.MaxValue ? short.MaxValue : _v < short.MinValue ? short.MinValue : _v;
            pcm[2 * sample] = (byte)(_v & 0xFF);
            pcm[2 * sample + 1] = (byte)((_v >> 8) & 0xFF);
        }
    }
}
