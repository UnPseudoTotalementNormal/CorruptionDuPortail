namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// NET-03 (epic-network-sync-hardening): how a player's pseudo is shown on every name surface (cards, InfoTable,
    /// chat, portal title). A missing roster row is never rendered as an empty string, and a player who left mid-game
    /// keeps their name with a marker (owner decisions, 2026-10-04).
    /// </summary>
    public static class PseudoDisplay
    {
        public const string MissingLabel = "Joueur ?";
        public const string LeftSuffix = " (parti)";

        public static string Format(bool hasEntry, string pseudo, bool hasLeft)
        {
            if (!hasEntry || string.IsNullOrWhiteSpace(pseudo))
            {
                return MissingLabel;
            }
            return hasLeft ? pseudo + LeftSuffix : pseudo;
        }
    }
}
