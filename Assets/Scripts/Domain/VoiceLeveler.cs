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
    /// <item>limiter: a peak that would pass <see cref="Ceiling"/> turns the whole voice down for a moment (the gain
    /// falls just before the peak, looking <see cref="LookaheadSeconds"/> ahead, and comes back over
    /// <see cref="ReleaseSeconds"/>): the wave keeps its shape. The first version bent every sample over -9 dBFS
    /// instead, which reshaped the peaks of a voice: a saturation of its own (playtest 2026-11-20, still saturated).</item>
    /// </list>
    /// Gains glide sample by sample inside a block, so nothing clicks. <see cref="Burst"/> measures what came in (was it
    /// already clipped when it arrived?) and what the limiter did (for the playtest logs). Pure: no Unity, no FMOD.
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
        /// <summary>The voice never goes past this (≈ -3 dBFS).</summary>
        public const double Ceiling = 0.7;
        /// <summary>How far ahead the limiter sees a peak coming, so its gain is down before it.</summary>
        public const double LookaheadSeconds = 0.0015;
        /// <summary>How long the limiter's gain takes to come back after a peak.</summary>
        public const double ReleaseSeconds = 0.08;
        // A sample the limiter lowered by more than this (1 dB) counts as limited in the burst stats.
        private const double LimitedGain = 0.89;

        private const double BlockSeconds = 0.01;
        // Per 10 ms block: the level follower rises fast (a louder voice is lowered within ~30 ms) and falls slowly
        // (~0.5 s), so the gain does not pump between syllables.
        private const double LevelAttack = 0.3;
        private const double LevelRelease = 0.02;
        // The gate opens at once and closes over ~50 ms.
        private const double GateClose = 0.2;
        private const short ClippedSample = 32000;

        private readonly int _blockSamples;
        private readonly int _lookahead;
        private readonly double _release;
        private double[] _scratch = Array.Empty<double>();
        private double[] _envelope = Array.Empty<double>();
        // The last limiter gains of the previous packet (the smoothing window spans packets).
        private readonly double[] _history;
        private double _limiterGain = 1.0;
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

            /// <summary>Loudest sample played, after the gain and the limiter.</summary>
            public double OutPeak;
            /// <summary>Samples the limiter turned down by more than 1 dB.</summary>
            public int Limited;

            public double SpeechRms => SpeechSamples > 0 ? Math.Sqrt(SpeechSquares / SpeechSamples) : 0;
            public double MeanGain => GainBlocks > 0 ? GainSum / GainBlocks : 1;
        }

        public VoiceLeveler(int sampleRate)
        {
            _blockSamples = Math.Max(1, (int)(sampleRate * BlockSeconds));
            _lookahead = Math.Max(1, (int)(sampleRate * LookaheadSeconds));
            _release = 1.0 - Math.Exp(-1.0 / (sampleRate * ReleaseSeconds));
            _history = new double[_lookahead];
            for (int i = 0; i < _lookahead; i++)
            {
                _history[i] = 1.0;
            }
        }

        public void ResetBurst() => Burst = default;

        /// <summary>Levels <paramref name="count"/> bytes of PCM16 mono little-endian in place.</summary>
        public void Process(byte[] pcm, int count)
        {
            int _samples = count / 2;
            if (_scratch.Length < _samples)
            {
                _scratch = new double[_samples];
                _envelope = new double[_samples];
            }
            for (int _from = 0; _from < _samples; _from += _blockSamples)
            {
                int _to = Math.Min(_samples, _from + _blockSamples);
                ProcessBlock(pcm, _from, _to);
            }
            LimitInto(pcm, _samples);
        }

        // The gained samples (_scratch) to PCM16 under the ceiling. Per sample: the gain that keeps it under the
        // ceiling, the lowest one over the next lookahead (so the gain is down before the peak), released slowly, then
        // averaged over the lookahead so it glides down instead of stepping. The average of gains that are each low
        // enough for the coming peak stays low enough; a last clamp only catches a peak at a packet's start that the
        // previous packet could not see.
        private void LimitInto(byte[] pcm, int samples)
        {
            for (int i = 0; i < samples; i++)
            {
                double _abs = Math.Abs(_scratch[i]);
                _envelope[i] = _abs > Ceiling ? Ceiling / _abs : 1.0;
            }
            // Lowest need over [i, i + lookahead], then the slow release, carried across packets.
            for (int i = 0; i < samples; i++)
            {
                double _need = _envelope[i];
                int _end = Math.Min(samples, i + _lookahead + 1);
                for (int j = i + 1; j < _end; j++)
                {
                    if (_envelope[j] < _need)
                    {
                        _need = _envelope[j];
                    }
                }
                _limiterGain = _need < _limiterGain ? _need : _limiterGain + (_need - _limiterGain) * _release;
                _envelope[i] = _limiterGain;
            }
            double _window = 0;
            for (int i = 0; i < _lookahead; i++)
            {
                _window += _history[i];
            }
            for (int i = 0; i < samples; i++)
            {
                double _oldest = i < _lookahead ? _history[i] : _envelope[i - _lookahead];
                _window += _envelope[i] - _oldest;
                double _gain = Math.Min(_window / _lookahead, 1.0);
                double _y = _scratch[i] * _gain;
                if (_y > Ceiling) _y = Ceiling;
                else if (_y < -Ceiling) _y = -Ceiling;
                if (_gain < LimitedGain)
                {
                    Burst.Limited++;
                }
                double _abs = Math.Abs(_y);
                if (_abs > Burst.OutPeak)
                {
                    Burst.OutPeak = _abs;
                }
                Write(pcm, i, _y);
            }
            // Keep the last gains for the next packet's window (a packet shorter than the window keeps older ones).
            var _last = new double[_lookahead];
            for (int i = 0; i < _lookahead; i++)
            {
                int _index = samples - _lookahead + i;
                _last[i] = _index >= 0 ? _envelope[_index] : _history[i + samples];
            }
            Array.Copy(_last, _history, _lookahead);
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
                _scratch[i] = Read(pcm, i) * _g;
            }
            _gain = _targetGain;
            _gate = _targetGate;
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
