using System;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure, engine-free liveness decision core (arch-liveness-heartbeat §8.1/§8.2). Holds a per-peer
    /// missed-beat counter and declares a peer lost on <c>missedBeats &gt;= threshold</c>. There is NO
    /// clock and NO notion of time inside — the decision is count-of-missed-beats, not seconds. The
    /// seconds-&gt;beats conversion lives in <see cref="LivenessThreshold"/>; the wall-clock cadence lives
    /// in the pump. This class is a deterministic EditMode unit.
    ///
    /// Semantics (§8.2, all locked):
    /// - Bots (<c>clientId &gt;= 100</c>) are NEVER enrolled — no entry ⇒ no possible false PeerLost.
    /// - Enrollment grace: a newly enrolled peer starts at <c>missedBeats = 0</c>.
    /// - Terminal single-shot eviction: on reaching the threshold, <see cref="PeerLost"/> fires exactly
    ///   once, then the peer is removed from the tracked set. A later <see cref="Tick"/> can never re-emit.
    /// - No resurrection (reconnection is out of scope): <see cref="RecordBeat"/> on an unknown /
    ///   already-lost clientId is a silent no-op — it does NOT re-insert the peer.
    /// - <see cref="Tick"/> is called ONLY by the pump, never from a frame loop (else framerate-dependent).
    /// </summary>
    public sealed class LivenessTracker
    {
        private readonly int _threshold;
        private readonly Dictionary<ulong, int> _missedBeats = new Dictionary<ulong, int>();

        // Reusable snapshot buffer so Tick() can mutate the dictionary while iterating, with zero
        // per-tick allocation and no System.Linq dependency.
        private readonly List<ulong> _tickSnapshot = new List<ulong>();

        /// <summary>Raised exactly once per peer, at the moment it is declared lost. Carries the peer's clientId.</summary>
        public event Action<ulong> PeerLost;

        /// <param name="threshold">
        /// Missed-beat count at which a peer is declared lost. Injected as a plain <c>int</c>
        /// (computed by <see cref="LivenessThreshold.FromSeconds"/>); the tracker never sees seconds.
        /// </param>
        public LivenessTracker(int threshold)
        {
            if (threshold < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(threshold), threshold, "Liveness threshold must be at least 1 beat.");
            }

            _threshold = threshold;
        }

        /// <summary>Number of peers currently tracked (excludes bots and lost/removed peers). For tests/diagnostics.</summary>
        public int TrackedCount => _missedBeats.Count;

        /// <summary>True while the peer has a live entry (enrolled, not yet lost or removed).</summary>
        public bool IsTracked(ulong clientId) => _missedBeats.ContainsKey(clientId);

        /// <summary>
        /// Enroll a peer with a fresh miss counter (enrollment grace, §8.2). No-op if the clientId is a
        /// simulated bot (<c>&gt;= 100</c>) or is already tracked — enrolling never resets an existing counter.
        /// </summary>
        public void Enroll(ulong clientId)
        {
            if (clientId >= LivenessThreshold.SimulatedBotClientIdFloor)
            {
                return; // Bots have no real socket and never pong — never enrolled (§8.2).
            }

            if (_missedBeats.ContainsKey(clientId))
            {
                return; // Idempotent enroll — do not stomp the running counter.
            }

            _missedBeats[clientId] = 0;
        }

        /// <summary>
        /// Voluntary removal (e.g. a graceful leave): drop the peer with no <see cref="PeerLost"/> emission.
        /// No-op on an unknown/already-gone clientId.
        /// </summary>
        public void RemovePeer(ulong clientId)
        {
            _missedBeats.Remove(clientId);
        }

        /// <summary>
        /// Record a received beat: reset that peer's miss counter to 0. Affects ONLY that peer. Silent
        /// no-op on an unknown / already-lost / bot clientId — never resurrects a removed peer (§8.2).
        /// </summary>
        public void RecordBeat(ulong clientId)
        {
            if (_missedBeats.ContainsKey(clientId))
            {
                _missedBeats[clientId] = 0;
            }
            // else: unknown/lost/bot — no resurrection, no insertion.
        }

        /// <summary>
        /// Advance one beat window: increment every tracked peer's miss counter. Any peer that reaches
        /// the threshold is declared lost — <see cref="PeerLost"/> fires exactly once, then the peer is
        /// removed (terminal). Called ONLY by the pump.
        /// </summary>
        public void Tick()
        {
            if (_missedBeats.Count == 0)
            {
                return;
            }

            // Snapshot keys so we can safely remove lost peers while processing.
            _tickSnapshot.Clear();
            foreach (var clientId in _missedBeats.Keys)
            {
                _tickSnapshot.Add(clientId);
            }

            List<ulong> lost = null;
            for (int i = 0; i < _tickSnapshot.Count; i++)
            {
                ulong clientId = _tickSnapshot[i];
                int missed = _missedBeats[clientId] + 1;
                _missedBeats[clientId] = missed;

                if (missed >= _threshold)
                {
                    if (lost == null)
                    {
                        lost = new List<ulong>();
                    }

                    lost.Add(clientId);
                }
            }

            if (lost == null)
            {
                return;
            }

            for (int i = 0; i < lost.Count; i++)
            {
                ulong clientId = lost[i];
                _missedBeats.Remove(clientId); // Terminal removal BEFORE emit — no re-entrant re-tick.
                PeerLost?.Invoke(clientId);
            }
        }
    }
}
