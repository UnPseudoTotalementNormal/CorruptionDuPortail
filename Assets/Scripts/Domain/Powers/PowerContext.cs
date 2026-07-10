namespace CorruptionDuPortail.Domain.Powers
{
    /// <summary>
    /// Read-only snapshot a power's decision reasons over: the caster (owner) logical slot, the picked
    /// target slot (if any), the owner's remaining uses, and a resolver for the power's own replicated
    /// state port. Plain values only — no NetworkManager, no MonoBehaviour, no singleton. Grows as more
    /// powers are migrated (e.g. game-state / character queries added as pure ports).
    /// </summary>
    public readonly struct PowerContext
    {
        public int OwnerSlot { get; }
        /// <summary>The picked target's logical slot, or -1 when the power takes no target.</summary>
        public int TargetSlot { get; }
        /// <summary>A second picked slot for char+role powers (the picked role's owner), or -1.</summary>
        public int SecondaryTargetSlot { get; }
        public int UsesLeft { get; }
        /// <summary>Read-only roster view (slots + faction + pseudo) for roster-reading powers. May be null.</summary>
        public IRosterView Roster { get; }

        private readonly IPowerStateResolver _state;

        public PowerContext(int ownerSlot, int targetSlot = -1, int secondaryTargetSlot = -1, int usesLeft = 1,
            IRosterView roster = null, IPowerStateResolver state = null)
        {
            OwnerSlot = ownerSlot;
            TargetSlot = targetSlot;
            SecondaryTargetSlot = secondaryTargetSlot;
            UsesLeft = usesLeft;
            Roster = roster;
            _state = state;
        }

        public bool HasTarget => TargetSlot >= 0;

        /// <summary>Resolve this power's replicated-state port (null if none wired — decisions that need one must guard).</summary>
        public TPort State<TPort>() where TPort : class => _state?.Resolve<TPort>();
    }
}
