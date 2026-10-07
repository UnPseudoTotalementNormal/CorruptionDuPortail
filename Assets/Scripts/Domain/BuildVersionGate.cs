namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// NET-05 (epic-network-sync-hardening): may a joiner's build play with the host's build? Two different player
    /// builds silently fail to deserialize each other's messages on the first wire change (e.g. a renamed type sent
    /// by assembly-qualified name), so they are refused with an explicit reason. An Editor on either side is allowed
    /// (version strings are meaningless in development / multiplayer play mode) but flagged for a warning log.
    /// </summary>
    public static class BuildVersionGate
    {
        /// <summary>Owner-approved wording (2026-10-04).</summary>
        public const string MismatchReason = "Version différente de l'hôte, mets ton jeu à jour.";

        public enum Verdict
        {
            Compatible,
            AllowedEditorMismatch,
            Rejected,
        }

        /// <param name="joiner">The joiner's connection payload, or null when it sent none (an older build).</param>
        public static Verdict Evaluate(string hostVersion, bool hostIsEditor, ConnectionPayload joiner)
        {
            bool _joinerIsEditor = joiner != null && joiner.IsEditor;
            string _joinerVersion = joiner?.BuildVersion ?? string.Empty;

            if (joiner != null && _joinerVersion == (hostVersion ?? string.Empty))
            {
                return Verdict.Compatible;
            }
            return hostIsEditor || _joinerIsEditor ? Verdict.AllowedEditorMismatch : Verdict.Rejected;
        }
    }
}
