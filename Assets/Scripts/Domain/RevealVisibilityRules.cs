namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure visibility rules for character-info reveals. No Unity / NGO dependencies so the
    /// decision is unit-testable in EditMode without a multi-client NGO harness.
    /// </summary>
    public static class RevealVisibilityRules
    {
        /// <summary>
        /// True when a reveal whose observer is <paramref name="viewerId"/> belongs to the
        /// local player, i.e. when the local UI (card flip + info-changed event) must refresh.
        /// </summary>
        /// <remarks>
        /// The reveal RPC runs only on the intended viewer (single-target for personal reveals,
        /// broadcast for public ones where every receiver is a viewer), so the caller must pass
        /// the real local client id here — not a storage sentinel such as 0, which equals the
        /// local id only on the host and silently skips the refresh on every other client.
        /// </remarks>
        public static bool ShouldRefreshLocalUi(ulong viewerId, ulong localClientId)
            => viewerId == localClientId;
    }
}
