namespace CorruptionDuPortail.Domain.Powers
{
    /// <summary>
    /// Stable identity of a power's decision logic. Append-only; each power's decision declares its
    /// <see cref="PowerId"/> and the catalog maps it back. Kept as an enum (compile-safe, greppable)
    /// rather than strings/GUIDs.
    /// </summary>
    public enum PowerId
    {
        None = 0,
        CorruptionParanoia = 1,
        CorruptionInsight = 2,
        CorruptionKnowledge = 3,
        EyeOfTheVoid = 4,
        AutoCorruption = 5,
    }
}
