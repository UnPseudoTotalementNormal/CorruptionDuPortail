using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Investigation join-load-timeout-kick: the client join wait must NOT kick a slow-but-honest GameScene
    /// load, yet must still fast-fail a dead/silent host and bound a genuinely stuck sync. These lock the pure
    /// two-phase decision (approval deadline applies only until sync starts; total deadline is the absolute cap).
    /// </summary>
    [Category("Networking")]
    public class ConnectHandshakePolicyTests
    {
        private const double Approval = 10.0;
        private const double Total = 90.0;

        private static ConnectWaitState Eval(double elapsed, bool syncStarted, bool isConnected, bool sessionAlive)
            => ConnectHandshakePolicy.Evaluate(elapsed, syncStarted, isConnected, sessionAlive, Approval, Total);

        // --- Happy paths -----------------------------------------------------------------------------

        [Test]
        public void Connected_Fast_ReturnsConnected()
        {
            ConnectWaitState _state = Eval(elapsed: 0.5, syncStarted: false, isConnected: true, sessionAlive: true);
            Assert.AreEqual(ConnectWaitOutcome.Connected, _state.Outcome);
            Assert.AreEqual(ConnectFailReason.None, _state.Reason);
        }

        [Test]
        public void SlowHonestLoad_SyncStarted_KeepsWaiting()
        {
            // 45s in, well past the 10s approval deadline, but sync HAS started and total cap not reached.
            ConnectWaitState _state = Eval(elapsed: 45.0, syncStarted: true, isConnected: false, sessionAlive: true);
            Assert.AreEqual(ConnectWaitOutcome.Waiting, _state.Outcome, "A slow honest load must not be kicked mid-load.");
        }

        [Test]
        public void SyncJustStarted_BeforeApprovalDeadline_KeepsWaiting()
        {
            ConnectWaitState _state = Eval(elapsed: 3.0, syncStarted: true, isConnected: false, sessionAlive: true);
            Assert.AreEqual(ConnectWaitOutcome.Waiting, _state.Outcome);
        }

        [Test]
        public void PreApprovalDeadline_NoSyncYet_KeepsWaiting()
        {
            ConnectWaitState _state = Eval(elapsed: 4.0, syncStarted: false, isConnected: false, sessionAlive: true);
            Assert.AreEqual(ConnectWaitOutcome.Waiting, _state.Outcome);
        }

        // --- Failure paths ---------------------------------------------------------------------------

        [Test]
        public void SessionEnded_FailsImmediately_RegardlessOfTimers()
        {
            ConnectWaitState _state = Eval(elapsed: 0.1, syncStarted: false, isConnected: false, sessionAlive: false);
            Assert.AreEqual(ConnectWaitOutcome.Failed, _state.Outcome);
            Assert.AreEqual(ConnectFailReason.SessionEnded, _state.Reason);
        }

        [Test]
        public void DeadHost_NoSync_PastApprovalDeadline_FailsApprovalTimeout()
        {
            ConnectWaitState _state = Eval(elapsed: 10.0, syncStarted: false, isConnected: false, sessionAlive: true);
            Assert.AreEqual(ConnectWaitOutcome.Failed, _state.Outcome);
            Assert.AreEqual(ConnectFailReason.ApprovalTimeout, _state.Reason);
        }

        [Test]
        public void StuckSync_PastTotalDeadline_FailsTotalTimeout()
        {
            ConnectWaitState _state = Eval(elapsed: 90.0, syncStarted: true, isConnected: false, sessionAlive: true);
            Assert.AreEqual(ConnectWaitOutcome.Failed, _state.Outcome);
            Assert.AreEqual(ConnectFailReason.TotalTimeout, _state.Reason);
        }

        // --- Precedence ------------------------------------------------------------------------------

        [Test]
        public void Connected_WinsOver_EveryTimeout()
        {
            // Past both deadlines AND session dead-ish, but connected — success must win.
            ConnectWaitState _state = Eval(elapsed: 999.0, syncStarted: false, isConnected: true, sessionAlive: false);
            Assert.AreEqual(ConnectWaitOutcome.Connected, _state.Outcome);
        }

        [Test]
        public void SessionEnded_WinsOver_ApprovalTimeout()
        {
            // Both would fail; SessionEnded is the more specific/earlier reason.
            ConnectWaitState _state = Eval(elapsed: 50.0, syncStarted: false, isConnected: false, sessionAlive: false);
            Assert.AreEqual(ConnectFailReason.SessionEnded, _state.Reason);
        }

        [Test]
        public void SyncStarted_CancelsApprovalDeadline_NotTotal()
        {
            // Exactly at the approval deadline but sync started ⇒ not an approval timeout; still waiting.
            ConnectWaitState _atApproval = Eval(elapsed: 10.0, syncStarted: true, isConnected: false, sessionAlive: true);
            Assert.AreEqual(ConnectWaitOutcome.Waiting, _atApproval.Outcome);

            // Same but sync NOT started ⇒ approval timeout fires.
            ConnectWaitState _noSync = Eval(elapsed: 10.0, syncStarted: false, isConnected: false, sessionAlive: true);
            Assert.AreEqual(ConnectFailReason.ApprovalTimeout, _noSync.Reason);
        }

        [Test]
        public void Boundaries_AreInclusive_OnDeadlines()
        {
            // Just under the approval deadline, no sync → still waiting.
            Assert.AreEqual(ConnectWaitOutcome.Waiting,
                Eval(elapsed: 9.999, syncStarted: false, isConnected: false, sessionAlive: true).Outcome);

            // Just under the total deadline, sync started → still waiting.
            Assert.AreEqual(ConnectWaitOutcome.Waiting,
                Eval(elapsed: 89.999, syncStarted: true, isConnected: false, sessionAlive: true).Outcome);
        }
    }
}
