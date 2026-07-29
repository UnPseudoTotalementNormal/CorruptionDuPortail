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
        ChainedByShadows = 6,
        TruthChains = 7,
        Blessing = 8,
        HighPriorityBounty = 9,
        CursedVision = 10,
        Omniscience = 11,
        InfiniteMessage = 12,
        EmbraceOfShadows = 13,
        LackOfAffection = 14,
        CorruptingMark = 15,
        Legacy = 16,
        Reincarnation = 17,
        BoundByInk = 18,
        ClandestineObservation = 19,
        VisionOfTheImpossible = 20,
        CardsShuffling = 21,
        PersonalBeacons = 22,
        MarqueHurluberluges = 23,
        DroolyHealing = 24,
        TargetedByReport = 25,
    }
}
