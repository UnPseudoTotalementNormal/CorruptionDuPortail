using Network;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode characterization of <see cref="ConnectionApprovalGate.ShouldApprove"/>, the pure decision
    /// behind the NGO connection-approval callback that fixes "Rejoindre une partie déjà en cours". Pins the
    /// three cases: no game loop yet (host-start / pre-scene window) → approve; in lobby → approve; game
    /// started → reject.
    /// </summary>
    [Category("ConnectionApprovalGate")]
    public class ConnectionApprovalGateTests
    {
        [Test]
        public void NoGameManager_IsApproved()
        {
            // Host's own local connection at StartHost time: GameScene (and GameManager) not loaded yet.
            Assert.IsTrue(ConnectionApprovalGate.ShouldApprove(_gameManagerPresent: false, _isInLobbyPhase: false));
        }

        [Test]
        public void InLobbyPhase_IsApproved()
        {
            Assert.IsTrue(ConnectionApprovalGate.ShouldApprove(_gameManagerPresent: true, _isInLobbyPhase: true));
        }

        [Test]
        public void GameInProgress_IsRejected()
        {
            Assert.IsFalse(ConnectionApprovalGate.ShouldApprove(_gameManagerPresent: true, _isInLobbyPhase: false));
        }
    }
}
