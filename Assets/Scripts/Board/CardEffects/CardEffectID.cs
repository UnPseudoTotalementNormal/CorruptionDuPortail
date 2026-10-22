namespace Board
{
    public enum CardEffectID
    {
        TechnoBeacon = 0,
        Blessing = 1,
        CursedVision = 2,
        // Local to Dr Gloubi / the Dryade (CorruptionIconDriver): this player has been healed.
        Healed = 3,
        // Local to the Orpheline: she visited this player with Manque d'affection (the card, never the role).
        OrphelineVisit = 4,
    }
}