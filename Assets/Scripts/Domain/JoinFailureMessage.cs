namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure, engine-free wording for a failed client join (investigation join-started-game-gate). The server
    /// fills <c>NetworkManager.DisconnectReason</c> when it REJECTS a connection — notably
    /// <c>ConnectionApprovalGate.GameInProgressReason</c> ("La partie a déjà commencé.") — so that reason must
    /// always win over the generic phase wording. Without this priority a rejected mid-game join surfaced as
    /// "Connexion à l'hôte perdue", which is what the joiner actually saw before the fix.
    ///
    /// No NGO here — the caller reads DisconnectReason and passes it in; this only decides the string. Mirrors
    /// <see cref="ConnectHandshakePolicy"/> (pure EditMode unit).
    /// </summary>
    public static class JoinFailureMessage
    {
        /// <summary>Shown when synchronization started but never finished — an honest load that got stuck.</summary>
        public const string StuckLoadMessage = "Connexion échouée : la partie a mis trop de temps à charger.";

        /// <summary>Shown when the host never answered at all within the approval window.</summary>
        public const string SilentHostMessage = "Connexion expirée : l'hôte n'a pas répondu.";

        /// <param name="_disconnectReason">The server-supplied rejection reason, or null/empty when there is none.</param>
        /// <param name="_reason">The phase in which the wait gave up, used only as a fallback.</param>
        /// <returns>The server reason when present, otherwise wording picked by failure phase.</returns>
        public static string Build(string _disconnectReason, ConnectFailReason _reason)
        {
            if (!string.IsNullOrEmpty(_disconnectReason))
            {
                return _disconnectReason;
            }

            return _reason == ConnectFailReason.TotalTimeout
                ? StuckLoadMessage
                : SilentHostMessage;
        }
    }
}
