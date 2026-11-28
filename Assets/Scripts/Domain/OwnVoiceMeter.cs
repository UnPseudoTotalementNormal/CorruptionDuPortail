using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// How open the speaker's OWN cat's mouth is (T22, CatTalk). His voice is never played back to him, so the packets
    /// he sends are decoded on his side, levelled like a listener does (<see cref="VoiceLeveler"/>: the same mouth the
    /// others see) and read back at the pace they were captured. After a silence the next packet starts at once
    /// (playtest 2026-11-20: Flox talked during an emote and his cat's mouth stayed shut on his screen).
    /// </summary>
    public sealed class OwnVoiceMeter
    {
        private readonly int _bytesPerSecond;
        private readonly VoiceLeveler _leveler;
        private readonly VoiceLevelTrack _levels;
        private long _written;
        private double _played;

        public OwnVoiceMeter(int sampleRate)
        {
            _bytesPerSecond = sampleRate * sizeof(short);
            _leveler = new VoiceLeveler(sampleRate);
            _levels = new VoiceLevelTrack(_bytesPerSecond / 50); // 20 ms blocks, like the playback
        }

        /// <summary>
        /// What Steam encoded from this player's own microphone since the last <see cref="ResetBurst"/>, before any
        /// processing: the same stats a listener logs, so a voice that arrives distorted can be traced to the
        /// speaker's side (it was already so here) or to the way between.
        /// </summary>
        public VoiceLeveler.BurstStats Burst => _leveler.Burst;

        /// <summary>Everything measured was read back: the player stopped talking.</summary>
        public bool Idle => (long)_played >= _written;

        public void ResetBurst() => _leveler.ResetBurst();

        /// <summary>Measures one decoded packet (PCM16 mono, levelled in place).</summary>
        public void Add(byte[] pcm, int count)
        {
            if (count <= 0)
            {
                return;
            }
            _leveler.Process(pcm, count);
            if (_written < (long)_played)
            {
                _written = (long)_played;
            }
            _levels.Add(_written, pcm, count);
            _written += count;
        }

        /// <summary>Moves the reader <paramref name="seconds"/> on and returns how open the mouth is (0 when nothing is left).</summary>
        public float Advance(double seconds)
        {
            _played = Math.Min(_written, _played + seconds * _bytesPerSecond);
            return (long)_played < _written ? (float)VoiceLevelTrack.Openness(_levels.LevelAt((long)_played)) : 0f;
        }
    }
}
