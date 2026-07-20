using CorruptionDuPortail.Domain;
using Network;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode characterization of <see cref="JoinFailureMessage.Build"/>, the pure wording decision behind a
    /// failed client join (investigation join-started-game-gate). Pins the priority that WAS the bug: a
    /// server-supplied DisconnectReason must always beat the generic phase wording, so a mid-game join reads
    /// "La partie a déjà commencé." and never "Connexion à l'hôte perdue".
    /// </summary>
    [Category("JoinFailureMessage")]
    public class JoinFailureMessageTests
    {
        [Test]
        public void DisconnectReason_BeatsEveryFailReason()
        {
            // The reject the gate actually emits — it must survive whatever phase the wait gave up in.
            string _reason = ConnectionApprovalGate.GameInProgressReason;

            Assert.AreEqual(_reason, JoinFailureMessage.Build(_reason, ConnectFailReason.SessionEnded));
            Assert.AreEqual(_reason, JoinFailureMessage.Build(_reason, ConnectFailReason.ApprovalTimeout));
            Assert.AreEqual(_reason, JoinFailureMessage.Build(_reason, ConnectFailReason.TotalTimeout));
            Assert.AreEqual(_reason, JoinFailureMessage.Build(_reason, ConnectFailReason.None));
        }

        [Test]
        public void NoDisconnectReason_TotalTimeout_ReadsAsStuckLoad()
        {
            Assert.AreEqual(
                JoinFailureMessage.StuckLoadMessage,
                JoinFailureMessage.Build(null, ConnectFailReason.TotalTimeout));
        }

        [Test]
        public void NoDisconnectReason_ApprovalTimeout_ReadsAsSilentHost()
        {
            Assert.AreEqual(
                JoinFailureMessage.SilentHostMessage,
                JoinFailureMessage.Build(null, ConnectFailReason.ApprovalTimeout));
        }

        [Test]
        public void EmptyDisconnectReason_FallsBackLikeNull()
        {
            // NGO leaves DisconnectReason as "" (not null) on a non-rejection teardown — treat both the same.
            Assert.AreEqual(
                JoinFailureMessage.SilentHostMessage,
                JoinFailureMessage.Build(string.Empty, ConnectFailReason.SessionEnded));
        }
    }
}
