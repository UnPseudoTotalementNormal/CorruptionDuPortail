using System;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// How loud one player's voice is at the moment the mixer plays it (T22, mockups vocal v2): the PCM16 written in
    /// the ring is measured in short blocks keyed by their absolute byte position (<see cref="VoiceRingCursor"/>
    /// counts), and read back at the reader's position, so the cat's mouth moves with what is heard, not with the
    /// packets' arrival. <see cref="Openness"/> maps a level to how open the mouth is.
    /// </summary>
    public sealed class VoiceLevelTrack
    {
        // Below this the mouth stays shut (room noise, the tail of a word); at the top it is fully open.
        private const double QuietDb = -42.0;
        private const double LoudDb = -14.0;

        private readonly int _blockBytes;
        private readonly Queue<Block> _blocks = new();

        private struct Block
        {
            public long Start;
            public long End;
            public double Rms;
        }

        /// <param name="blockBytes">Bytes per measured block (even: whole PCM16 samples), ~20 ms of voice.</param>
        public VoiceLevelTrack(int blockBytes)
        {
            _blockBytes = Math.Max(2, blockBytes & ~1);
        }

        /// <summary>Measures <paramref name="count"/> bytes of PCM16 mono written at absolute position <paramref name="start"/>.</summary>
        public void Add(long start, byte[] pcm, int count)
        {
            for (int _from = 0; _from + 1 < count; _from += _blockBytes)
            {
                int _to = Math.Min(count, _from + _blockBytes) & ~1;
                double _sum = 0;
                int _samples = 0;
                for (int i = _from; i + 1 < _to; i += 2)
                {
                    double _s = (short)(pcm[i] | pcm[i + 1] << 8) / 32768.0;
                    _sum += _s * _s;
                    _samples++;
                }
                if (_samples == 0)
                {
                    continue;
                }
                _blocks.Enqueue(new Block { Start = start + _from, End = start + _to, Rms = Math.Sqrt(_sum / _samples) });
            }
        }

        /// <summary>RMS (0..1) of the voice at absolute position <paramref name="played"/>; 0 in a gap or past the end.
        /// Forgets what the reader passed.</summary>
        public double LevelAt(long played)
        {
            while (_blocks.Count > 0 && _blocks.Peek().End <= played)
            {
                _blocks.Dequeue();
            }
            return _blocks.Count > 0 && _blocks.Peek().Start <= played ? _blocks.Peek().Rms : 0.0;
        }

        /// <summary>How open the mouth is (0..1) for a voice RMS, on a decibel scale.</summary>
        public static double Openness(double rms)
        {
            if (rms <= 0)
            {
                return 0;
            }
            double _db = 20.0 * Math.Log10(rms);
            return Math.Max(0, Math.Min(1, (_db - QuietDb) / (LoudDb - QuietDb)));
        }

        /// <summary>
        /// The mouth's scale: it follows the voice (R1) with a little constant fidget on top while it is open (Poyo:
        /// « un minimum de R2, juste pour ajouter de la vie »). <paramref name="fidget"/> is the share of the size that
        /// wobbles (0..1), <paramref name="seconds"/> any running clock.
        /// </summary>
        public static double MouthScale(double openness, double seconds, double fidget)
        {
            if (openness <= 0)
            {
                return 0;
            }
            double _wobble = Math.Abs(Math.Sin(seconds * 23.0) * Math.Sin(seconds * 14.5 + 2.0));
            return openness * (1.0 - fidget + fidget * _wobble);
        }
    }
}
