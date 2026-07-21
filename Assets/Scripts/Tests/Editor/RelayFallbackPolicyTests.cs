using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Investigation vpn-instant-disconnect (backlog #7): the dtls → wss fallback must retry ONLY on
    /// connectivity-shaped failures. These pin the pure retry verdict — a server rejection (filled
    /// DisconnectReason) or a stuck-but-started sync must NEVER burn a second 30 s attempt on a protocol
    /// change that cannot alter the outcome.
    /// </summary>
    [Category("Networking")]
    public class RelayFallbackPolicyTests
    {
        // --- Retry paths (connectivity-shaped) -------------------------------------------------------

        [Test]
        public void ApprovalTimeout_NoServerReason_Retries()
        {
            Assert.IsTrue(RelayFallbackPolicy.ShouldRetryNextProtocol(ConnectFailReason.ApprovalTimeout, _hasServerReason: false));
        }

        [Test]
        public void ApprovalTimeout_StaleServerReason_StillRetries()
        {
            // A timeout means no verdict arrived THIS attempt — a stale DisconnectReason string must not block the fallback.
            Assert.IsTrue(RelayFallbackPolicy.ShouldRetryNextProtocol(ConnectFailReason.ApprovalTimeout, _hasServerReason: true));
        }

        [Test]
        public void SessionEnded_NoServerReason_Retries()
        {
            // Transport give-up with no server verdict = the wire never worked — exactly the wss case.
            Assert.IsTrue(RelayFallbackPolicy.ShouldRetryNextProtocol(ConnectFailReason.SessionEnded, _hasServerReason: false));
        }

        // --- No-retry paths (deliberate outcomes) ----------------------------------------------------

        [Test]
        public void SessionEnded_WithServerReason_DoesNotRetry()
        {
            // The server answered (e.g. "La partie a déjà commencé.") — a protocol change cannot alter its verdict.
            Assert.IsFalse(RelayFallbackPolicy.ShouldRetryNextProtocol(ConnectFailReason.SessionEnded, _hasServerReason: true));
        }

        [Test]
        public void TotalTimeout_DoesNotRetry_RegardlessOfReason()
        {
            // Sync STARTED, so the wire worked — the stall is not connectivity-shaped.
            Assert.IsFalse(RelayFallbackPolicy.ShouldRetryNextProtocol(ConnectFailReason.TotalTimeout, _hasServerReason: false));
            Assert.IsFalse(RelayFallbackPolicy.ShouldRetryNextProtocol(ConnectFailReason.TotalTimeout, _hasServerReason: true));
        }

        [Test]
        public void None_DoesNotRetry_RegardlessOfReason()
        {
            // Success is never a retry trigger.
            Assert.IsFalse(RelayFallbackPolicy.ShouldRetryNextProtocol(ConnectFailReason.None, _hasServerReason: false));
            Assert.IsFalse(RelayFallbackPolicy.ShouldRetryNextProtocol(ConnectFailReason.None, _hasServerReason: true));
        }

        // --- HasServerReason discriminator -----------------------------------------------------------
        // NGO fills DisconnectReason on EVERY client-side transport disconnect with a
        // "[Disconnect Event]…"-prefixed placeholder; only a raw server-sent reason may veto the fallback.

        [Test]
        public void HasServerReason_NullOrEmpty_False()
        {
            Assert.IsFalse(RelayFallbackPolicy.HasServerReason(null));
            Assert.IsFalse(RelayFallbackPolicy.HasServerReason(string.Empty));
        }

        [Test]
        public void HasServerReason_TransportGeneratedPlaceholder_False()
        {
            // Shape produced by NetworkConnectionManager.GenerateDisconnectInformation on a transport give-up.
            Assert.IsFalse(RelayFallbackPolicy.HasServerReason(
                "[Disconnect Event][Client-0][TransportClientId-1][PeerDisconnected] The connection was closed."));
        }

        [Test]
        public void HasServerReason_ServerSentReason_True()
        {
            // A DisconnectReasonMessage payload is surfaced RAW (ConnectionApprovalGate.GameInProgressReason).
            Assert.IsTrue(RelayFallbackPolicy.HasServerReason("La partie a déjà commencé."));
        }
    }
}
