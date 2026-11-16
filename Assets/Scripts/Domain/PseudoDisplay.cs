using CorruptionDuPortail.Domain.Localization;
namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// NET-03 (epic-network-sync-hardening): how a player's pseudo is shown on every name surface (cards, InfoTable,
    /// chat, portal title). A missing roster row is never rendered as an empty string, and a player who left mid-game
    /// keeps their name with a marker (owner decisions, 2026-10-04). The result is in the language of THIS machine
    /// (T36): a text sent to other players carries the raw pseudo instead.
    /// </summary>
    public static class PseudoDisplay
    {
        public const string MissingLabel = "Joueur ?"; // loc
        public const string LeftSuffix = " (parti)";

        public static string Format(bool hasEntry, string pseudo, bool hasLeft)
        {
            if (!hasEntry || string.IsNullOrWhiteSpace(pseudo))
            {
                return Loc.Tr(MissingLabel);
            }
            // The French source reads pseudo + LeftSuffix: keep the two in step.
            return hasLeft ? Loc.Tr("{0} (parti)", pseudo) : pseudo;
        }
    }
}
