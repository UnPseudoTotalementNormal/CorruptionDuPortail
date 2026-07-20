using Network;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// [LEAVE][PHASE 3] (epic-player-leave-stability) — fast EditMode proof of the graceful-vs-abrupt
    /// DECISION core that <c>ClientDisconnectHandler</c> keys on when the local client's NGO session stops.
    /// The live handler wires <c>OnClientStopped</c> / <c>OnTransportFailure</c> to
    /// <see cref="HostDropPolicy.ShouldNotifyHostLoss"/>: the host-loss notification + return-to-menu fire
    /// iff the local instance was a pure client AND the shutdown was NOT expected (a graceful ShutOffGame
    /// or a self-initiated leave flags it expected). This pins that boundary with no NGO host — the full
    /// callback/return-to-menu flow over a live NetworkManager is a play-mode manual/Phase-5 check.
    /// </summary>
    [Category("LeaveStability")]
    public class HostDropPolicyTests
    {
        [Test]
        public void PureClient_UnexpectedStop_NotifiesHostLoss()
        {
            // Abrupt host crash / Alt-F4: a pure client's session ends with no expected-shutdown flag.
            Assert.IsTrue(HostDropPolicy.ShouldNotifyHostLoss(wasPureClient: true, expectedShutdown: false),
                "A pure client that loses the session unexpectedly MUST be notified of the host loss.");
        }

        [Test]
        public void PureClient_ExpectedStop_DoesNotNotify()
        {
            // Graceful ShutOffGame or a self-initiated leave flagged the shutdown as expected.
            Assert.IsFalse(HostDropPolicy.ShouldNotifyHostLoss(wasPureClient: true, expectedShutdown: true),
                "A graceful / self-initiated shutdown MUST NOT surface the abrupt host-loss notification.");
        }

        [Test]
        public void Host_UnexpectedStop_DoesNotNotify()
        {
            // The host is never treated as a host-loss victim — its teardown is owned by ShutOffGame.
            Assert.IsFalse(HostDropPolicy.ShouldNotifyHostLoss(wasPureClient: false, expectedShutdown: false),
                "The host stopping is not a client-side host loss — it owns its own return-to-menu.");
        }

        [Test]
        public void Host_ExpectedStop_DoesNotNotify()
        {
            Assert.IsFalse(HostDropPolicy.ShouldNotifyHostLoss(wasPureClient: false, expectedShutdown: true),
                "A host with an expected shutdown never raises the client host-loss notification.");
        }

        [Test]
        public void PureClient_StopDuringJoinHandshake_DoesNotNotify()
        {
            // investigation join-started-game-gate: a join REJECTED by ConnectionApprovalGate stops NGO exactly
            // like a host drop. The menu owns that failure and shows the server's reason ("La partie a déjà
            // commencé."), so this layer must not overwrite it with the generic host-loss wording.
            Assert.IsFalse(
                HostDropPolicy.ShouldNotifyHostLoss(
                    wasPureClient: true, expectedShutdown: false, joinHandshakeInProgress: true),
                "A stop while the menu is still awaiting a join verdict is a rejected join, not a host loss.");
        }

        [Test]
        public void PureClient_StopAfterJoinHandshakeClosed_StillNotifies()
        {
            // The window is closed in a finally, so a successful join cannot latch it and mute a real host loss.
            Assert.IsTrue(
                HostDropPolicy.ShouldNotifyHostLoss(
                    wasPureClient: true, expectedShutdown: false, joinHandshakeInProgress: false),
                "Once the join settled, an abrupt host loss MUST still be notified.");
        }
    }
}
