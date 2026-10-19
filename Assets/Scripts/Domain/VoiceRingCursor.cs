namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Book-keeping of one player's voice in a looping PCM ring (T22): the mixer reads the ring on its own clock while
    /// network packets arrive in bursts. Counts are absolute bytes (never wrapped), offsets are ring positions.
    /// The writer keeps a small lead on the reader: on a starvation (the speaker paused, a packet was lost) the next
    /// packet starts again <c>prebuffer</c> ahead, and a packet that would push the lead past <c>maxLead</c> (late
    /// burst) is dropped so the delay never grows. Every byte the reader passed must be cleared by the caller, so a
    /// region not written yet is always silence.
    /// </summary>
    public sealed class VoiceRingCursor
    {
        private readonly int _ringBytes;
        private readonly int _prebufferBytes;
        private readonly int _maxLeadBytes;
        private int _lastPosition;

        public VoiceRingCursor(int ringBytes, int prebufferBytes, int maxLeadBytes)
        {
            _ringBytes = ringBytes;
            _prebufferBytes = prebufferBytes;
            _maxLeadBytes = maxLeadBytes < ringBytes ? maxLeadBytes : ringBytes;
        }

        /// <summary>Bytes the reader consumed since the start.</summary>
        public long Played { get; private set; }

        /// <summary>Bytes written since the start (the write head, may lag behind <see cref="Played"/> when starved).</summary>
        public long Written { get; private set; }

        /// <summary>Nothing left to play: the caller may pause the reader.</summary>
        public bool Starved => Written <= Played;

        /// <summary>
        /// The reader is at ring <paramref name="position"/>. Returns the region it passed since the last call, to clear.
        /// </summary>
        public void Advance(int position, out int clearOffset, out int clearLength)
        {
            int _delta = ((position - _lastPosition) % _ringBytes + _ringBytes) % _ringBytes;
            clearOffset = (int)(Played % _ringBytes);
            clearLength = _delta;
            Played += _delta;
            _lastPosition = position;
        }

        /// <summary>
        /// Where to write <paramref name="count"/> bytes. False = drop the packet (it would make the delay grow).
        /// </summary>
        public bool TryPrepareWrite(int count, out int offset)
        {
            if (Starved)
            {
                Written = Played + _prebufferBytes;
            }
            offset = (int)(Written % _ringBytes);
            return Written + count - Played <= _maxLeadBytes;
        }

        /// <summary>The bytes prepared by <see cref="TryPrepareWrite"/> were written.</summary>
        public void Commit(int count) => Written += count;
    }
}
