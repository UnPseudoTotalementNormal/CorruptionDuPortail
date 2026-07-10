namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Domain mirror of the chat-window ids a power decision references (verbatim ChatWindowIDs values),
    /// so decisions stay pure without importing the engine enum.
    /// </summary>
    public static class ChatWindows
    {
        public const int Server = -1;
        public const int AnomalyOnly = 1;
    }
}
