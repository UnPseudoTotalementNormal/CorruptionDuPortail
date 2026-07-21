namespace CorruptionDuPortail.Domain.PlayerIcons
{
    /// <summary>
    /// How long a player-icon marker survives. Pure Domain data (no engine types) — the server-side
    /// PlayerIconManager reads it when a new awakening starts and purges only the transient ones.
    /// </summary>
    public enum PlayerIconLifetime
    {
        /// <summary>The marker stays until it is explicitly removed (or the match ends).</summary>
        Persistent = 0,

        /// <summary>The marker is dropped when the next awakening starts.</summary>
        ClearAtAwakeningStart = 1
    }
}
