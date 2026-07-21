namespace Network
{
    /// <summary>
    /// Outcome of a full <see cref="RelayConnector.ConnectClientAsync"/> run (all protocol attempts included).
    /// <see cref="FailureMessage"/> is built BEFORE any teardown (NetworkManager.Shutdown may clear
    /// DisconnectReason) and is null when no player-facing wording applies (allocation/service errors are
    /// already logged; the caller falls back to its generic failure path).
    /// </summary>
    public readonly struct RelayConnectResult
    {
        public readonly bool Success;
        public readonly string FailureMessage;

        private RelayConnectResult(bool _success, string _failureMessage)
        {
            Success = _success;
            FailureMessage = _failureMessage;
        }

        public static RelayConnectResult Succeeded()
        {
            return new RelayConnectResult(true, null);
        }

        public static RelayConnectResult Failed(string _failureMessage)
        {
            return new RelayConnectResult(false, _failureMessage);
        }
    }
}
