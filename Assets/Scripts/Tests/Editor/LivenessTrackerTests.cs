using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story B1 — pure EditMode battery for <see cref="LivenessTracker"/> (arch-liveness-heartbeat §8.8,
    /// cases 1-8 and 10-12). Driven entirely through <see cref="ManualLivenessPump"/> (beat count), zero
    /// <c>WaitForSeconds</c>, no clock — the tracker's decision is frame- and wall-clock-independent.
    /// </summary>
    [Category("Liveness")]
    public class LivenessTrackerTests
    {
        private const int Threshold = 5;

        private static LivenessTracker NewTracker(int threshold, out List<ulong> lost)
        {
            var tracker = new LivenessTracker(threshold);
            var captured = new List<ulong>();
            tracker.PeerLost += id => captured.Add(id);
            lost = captured;
            return tracker;
        }

        // 1
        [Test]
        public void RecordBeat_ResetsMissedCounter_ForThatPeerOnly()
        {
            var tracker = NewTracker(Threshold, out var lost);
            tracker.Enroll(1);
            tracker.Enroll(2);
            var pump = new ManualLivenessPump(tracker);

            pump.AdvanceBeats(Threshold - 1); // both at threshold-1, neither lost yet
            Assert.IsEmpty(lost);

            tracker.RecordBeat(1); // resets peer 1 only

            pump.AdvanceBeats(1); // peer 2 now hits threshold, peer 1 does not
            Assert.AreEqual(new List<ulong> { 2 }, lost, "Only the un-beaten peer should be lost.");
            Assert.IsTrue(tracker.IsTracked(1), "Peer 1's beat should have spared it.");
        }

        // 2
        [Test]
        public void Tick_IncrementsMissedBeats_ForAllTrackedPeers()
        {
            var tracker = NewTracker(Threshold, out var lost);
            tracker.Enroll(1);
            tracker.Enroll(2);
            tracker.Enroll(3);
            var pump = new ManualLivenessPump(tracker);

            pump.AdvanceBeats(Threshold); // all three cross together on the same tick

            CollectionAssert.AreEquivalent(new List<ulong> { 1, 2, 3 }, lost,
                "Every tracked peer's counter must advance on each Tick.");
        }

        // 3
        [Test]
        public void Tick_BelowThreshold_DoesNotEmitPeerLost()
        {
            var tracker = NewTracker(Threshold, out var lost);
            tracker.Enroll(1);
            var pump = new ManualLivenessPump(tracker);

            pump.AdvanceBeats(Threshold - 1);

            Assert.IsEmpty(lost, "No peer may be declared lost before the threshold is reached.");
            Assert.IsTrue(tracker.IsTracked(1));
        }

        // 4
        [Test]
        public void Tick_ReachingThreshold_EmitsPeerLostExactlyOnce()
        {
            var tracker = NewTracker(Threshold, out var lost);
            tracker.Enroll(1);
            var pump = new ManualLivenessPump(tracker);

            pump.AdvanceBeats(Threshold);

            Assert.AreEqual(new List<ulong> { 1 }, lost, "PeerLost must fire exactly once at the threshold.");
            Assert.IsFalse(tracker.IsTracked(1), "The peer must be removed on eviction (terminal).");
        }

        // 5
        [Test]
        public void Tick_PastThreshold_DoesNotReEmitPeerLost()
        {
            var tracker = NewTracker(Threshold, out var lost);
            tracker.Enroll(1);
            var pump = new ManualLivenessPump(tracker);

            pump.AdvanceBeats(Threshold + 10); // keep ticking well past eviction

            Assert.AreEqual(new List<ulong> { 1 }, lost,
                "A removed peer must never re-emit PeerLost on later ticks (terminal removal).");
        }

        // 6
        [Test]
        public void RecordBeat_OnAlreadyLostPeer_IsNoOp_DoesNotResurrect()
        {
            var tracker = NewTracker(Threshold, out var lost);
            tracker.Enroll(1);
            var pump = new ManualLivenessPump(tracker);

            pump.AdvanceBeats(Threshold); // peer 1 lost + removed
            Assert.AreEqual(new List<ulong> { 1 }, lost);

            tracker.RecordBeat(1); // must not re-insert

            Assert.IsFalse(tracker.IsTracked(1), "RecordBeat must not resurrect an already-lost peer.");
            pump.AdvanceBeats(Threshold);
            Assert.AreEqual(new List<ulong> { 1 }, lost, "No second PeerLost after a no-op RecordBeat.");
        }

        // 7 — decision: RecordBeat on an unknown (never-enrolled) peer is a silent no-op (§8.2), NOT
        // auto-registration. Enrollment is explicit via Enroll(); reconnection/resurrection is out of scope.
        [Test]
        public void RecordBeat_OnUnknownPeer_IsIgnored()
        {
            var tracker = NewTracker(Threshold, out var lost);

            tracker.RecordBeat(42); // never enrolled

            Assert.IsFalse(tracker.IsTracked(42), "RecordBeat must not auto-register an unknown peer.");
            Assert.AreEqual(0, tracker.TrackedCount);

            var pump = new ManualLivenessPump(tracker);
            pump.AdvanceBeats(Threshold * 2);
            Assert.IsEmpty(lost, "An un-enrolled peer can never be declared lost.");
        }

        // 8
        [Test]
        public void SimulatedBot_ClientIdOver100_NeverDeclaredLost()
        {
            var tracker = NewTracker(Threshold, out var lost);

            tracker.Enroll(100); // exactly at the bot floor
            tracker.Enroll(150);
            Assert.AreEqual(0, tracker.TrackedCount, "Bots (>=100) are never enrolled.");

            var pump = new ManualLivenessPump(tracker);
            pump.AdvanceBeats(Threshold * 3);

            Assert.IsEmpty(lost, "A simulated bot must never be declared lost.");
        }

        // 10
        [Test]
        public void RemovePeer_StopsTicking()
        {
            var tracker = NewTracker(Threshold, out var lost);
            tracker.Enroll(1);
            var pump = new ManualLivenessPump(tracker);

            pump.AdvanceBeats(Threshold - 1);
            tracker.RemovePeer(1); // voluntary leave before eviction

            Assert.IsFalse(tracker.IsTracked(1));
            pump.AdvanceBeats(Threshold * 2);

            Assert.IsEmpty(lost, "A voluntarily-removed peer must not emit a late PeerLost.");
        }

        // 11
        [Test]
        public void MultiPeer_IndependentCounters()
        {
            var tracker = NewTracker(Threshold, out var lost);
            tracker.Enroll(1);
            tracker.Enroll(2);
            var pump = new ManualLivenessPump(tracker);

            // Keep peer 1 alive by beating it every window; let peer 2 starve.
            for (int i = 0; i < Threshold; i++)
            {
                tracker.RecordBeat(1);
                pump.AdvanceBeats(1);
            }

            Assert.AreEqual(new List<ulong> { 2 }, lost, "Only the starved peer should be lost.");
            Assert.IsTrue(tracker.IsTracked(1), "The continuously-beaten peer must survive.");
        }

        // 12
        [Test]
        public void PeerLost_CarriesCorrectClientId()
        {
            var tracker = NewTracker(Threshold, out var lost);
            const ulong target = 42; // distinctive but below the bot floor (100), so it is actually enrolled
            tracker.Enroll(target);
            var pump = new ManualLivenessPump(tracker);

            pump.AdvanceBeats(Threshold);

            Assert.AreEqual(new List<ulong> { target }, lost, "PeerLost must carry the exact clientId that was lost.");
        }
    }
}
