using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure, decision-only power eligibility (Story 11.1 — continues the Wave 1-4 / Epic 4 extraction).
    /// Mirrors the base <c>Power.CanUse</c> rule chain EXACTLY: the ordered set of disqualifiers that
    /// gate whether a power may be used, as a POCO over plain values, so the rules are EditMode-testable
    /// without booting NGO. No engine types — the adapter reads the owner Character's NetworkVariables /
    /// the power's own state and passes plain bools/ints. The expensive, engine-coupled "is there at least
    /// one valid target" check is supplied as a LAZY delegate so its exact short-circuit position (and thus
    /// any exception behaviour) is preserved: it runs only when a target-selecting power has already passed
    /// every earlier rule, matching the original <c>needTargetSelection &amp;&amp; GetValidTargets().Count &lt;= 0</c>.
    /// Decision-only (NFR4): returns the verdict; the adapter owns all state and side effects (the owner-null
    /// warning, the use-count decrement, the target enumeration).
    /// </summary>
    public sealed class PowerUsability
    {
        /// <summary>
        /// Evaluates the base usability rule chain in the SAME order as the original <c>Power.CanUse</c>.
        /// The owner-null guard (and its warning log) stays in the adapter — this method is only reached
        /// with a live owner character. <paramref name="hasAtLeastOneValidTarget"/> is invoked lazily and
        /// ONLY when a target-selecting power has passed every earlier rule.
        /// </summary>
        public bool CanUse(in PowerUsabilityContext context, Func<bool> hasAtLeastOneValidTarget)
        {
            if (!context.AllComponentsAllowUse) return false;
            if (context.IsPassive) return false;
            if (context.IsCurrentlyUsed && !context.IgnoreCurrentlyUsed) return false;
            if (context.IsChained || context.IsEliminated) return false;
            if (context.HasToBeAwakened && !context.IsAwakened) return false;
            if (context.NeedsTargetSelection && !hasAtLeastOneValidTarget()) return false;
            if (context.PowerUsesLeft <= 0) return false;

            return true;
        }
    }

    /// <summary>
    /// Plain-value snapshot of the engine state that the base <c>Power.CanUse</c> rule chain reads. Built
    /// by the adapter from the owner Character's NetworkVariables and the power's own fields.
    /// </summary>
    public readonly struct PowerUsabilityContext
    {
        public readonly bool AllComponentsAllowUse;
        public readonly bool IsPassive;
        public readonly bool IsCurrentlyUsed;
        public readonly bool IgnoreCurrentlyUsed;
        public readonly bool IsChained;
        public readonly bool IsEliminated;
        public readonly bool HasToBeAwakened;
        public readonly bool IsAwakened;
        public readonly bool NeedsTargetSelection;
        public readonly int PowerUsesLeft;

        public PowerUsabilityContext(
            bool allComponentsAllowUse,
            bool isPassive,
            bool isCurrentlyUsed,
            bool ignoreCurrentlyUsed,
            bool isChained,
            bool isEliminated,
            bool hasToBeAwakened,
            bool isAwakened,
            bool needsTargetSelection,
            int powerUsesLeft)
        {
            AllComponentsAllowUse = allComponentsAllowUse;
            IsPassive = isPassive;
            IsCurrentlyUsed = isCurrentlyUsed;
            IgnoreCurrentlyUsed = ignoreCurrentlyUsed;
            IsChained = isChained;
            IsEliminated = isEliminated;
            HasToBeAwakened = hasToBeAwakened;
            IsAwakened = isAwakened;
            NeedsTargetSelection = needsTargetSelection;
            PowerUsesLeft = powerUsesLeft;
        }
    }
}
