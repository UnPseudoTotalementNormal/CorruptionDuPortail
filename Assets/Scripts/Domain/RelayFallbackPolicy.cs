namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure retry verdict for the Relay connection-type fallback (investigation vpn-instant-disconnect,
    /// backlog #7). Decides whether a failed connect attempt is CONNECTIVITY-shaped — worth retrying on the
    /// next protocol (dtls → wss) — or a DELIBERATE outcome that a different transport protocol cannot change.
    /// </summary>
    public static class RelayFallbackPolicy
    {
        /// <summary>
        /// NGO 2.12 fills <c>NetworkManager.DisconnectReason</c> on EVERY client-side transport disconnect —
        /// not only on server rejections — with a message it prefixes with this header
        /// (<c>NetworkConnectionManager.GenerateDisconnectInformation</c>). A genuine server-supplied reason
        /// (<c>DisconnectReasonMessage</c>, e.g. "La partie a déjà commencé.") is returned RAW, without the
        /// header. Pinned to the NGO version; if the format ever changes, the silent-timeout path
        /// (ApprovalTimeout) still covers the fallback.
        /// </summary>
        public const string TransportGeneratedReasonPrefix = "[Disconnect Event]";

        /// <summary>
        /// True only when <paramref name="_disconnectReason"/> is a reason the SERVER deliberately sent —
        /// non-empty and not a transport-generated "[Disconnect Event]…" placeholder. A transport give-up is
        /// connectivity, not a verdict.
        /// </summary>
        public static bool HasServerReason(string _disconnectReason)
        {
            if (string.IsNullOrEmpty(_disconnectReason))
            {
                return false;
            }

            return !_disconnectReason.StartsWith(TransportGeneratedReasonPrefix, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the failure looks like the host was never reachable: no approval ever arrived
        /// (<see cref="ConnectFailReason.ApprovalTimeout"/>), or the transport gave up with no server-supplied
        /// reason (<see cref="ConnectFailReason.SessionEnded"/> with <paramref name="_hasServerReason"/> false —
        /// compute it via <see cref="HasServerReason"/>). A server reason means the server DID answer — a
        /// protocol change cannot alter its verdict. <see cref="ConnectFailReason.TotalTimeout"/> means
        /// synchronization STARTED, so the wire worked; retrying another protocol would only double the wait.
        /// </summary>
        public static bool ShouldRetryNextProtocol(ConnectFailReason _reason, bool _hasServerReason)
        {
            if (_reason == ConnectFailReason.ApprovalTimeout)
            {
                return true;
            }

            return _reason == ConnectFailReason.SessionEnded && !_hasServerReason;
        }
    }
}
