namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// The online stack a player build runs. Both ship from the same code (CI builds one of each):
    /// <see cref="Relay"/> = UGS sign-in, UGS lobbies and Unity Relay (several copies can run on one PC, for tests);
    /// <see cref="Steam"/> = Steam profile, Steam lobbies and the Steam Datagram Relay (needs the Steam client).
    /// The two never meet: a Relay build cannot see nor reach a Steam game, and the other way round.
    /// </summary>
    public enum OnlineFlavor
    {
        Relay,
        Steam,
    }

    public static class OnlineFlavorRules
    {
        /// <summary>
        /// May this build offer to rejoin a game saved with <paramref name="hostKind"/> (RejoinSessionStore)? Both
        /// builds share the PC's PlayerPrefs, so a game left in one build must not show up in the other.
        /// </summary>
        public static bool CanRejoin(OnlineFlavor flavor, string hostKind)
        {
            return flavor == OnlineFlavor.Steam
                ? hostKind == "steam"
                : hostKind == "relay" || hostKind == "direct";
        }
    }
}
