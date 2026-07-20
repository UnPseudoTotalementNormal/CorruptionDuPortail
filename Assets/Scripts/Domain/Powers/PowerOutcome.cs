using System;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain.Powers
{
    /// <summary>
    /// The pure result of a power's decision: whether it fired, the ORDERED effect intentions it
    /// produced, and how many uses it consumed. Value-typed and side-effect-free — the runtime holder
    /// applies the effects and writes any next-state; the decision itself mutates nothing.
    /// </summary>
    public readonly struct PowerOutcome
    {
        public bool Accepted { get; }
        public IReadOnlyList<EffectDescriptor> Effects { get; }
        public int UsesConsumed { get; }
        /// <summary>Debug-only reason when <see cref="Accepted"/> is false.</summary>
        public string RejectReason { get; }
        /// <summary>
        /// Whether the caster's guess/target was right — the caster-facing "did it work?" signal. Defaults to
        /// <see cref="PowerVerdict.None"/> so unconditional-effect powers stay silent. NOT a synonym for
        /// <see cref="Accepted"/>: a verdict-bearing decision Accepts on both its branches.
        /// </summary>
        public PowerVerdict Verdict { get; }

        private PowerOutcome(bool accepted, IReadOnlyList<EffectDescriptor> effects, int usesConsumed,
            string rejectReason, PowerVerdict verdict)
        {
            Accepted = accepted;
            Effects = effects ?? Array.Empty<EffectDescriptor>();
            UsesConsumed = usesConsumed;
            RejectReason = rejectReason;
            Verdict = verdict;
        }

        public static PowerOutcome Accept(IReadOnlyList<EffectDescriptor> effects, int usesConsumed = 1)
            => new(true, effects, usesConsumed, null, PowerVerdict.None);

        public static PowerOutcome Accept(params EffectDescriptor[] effects)
            => new(true, effects, 1, null, PowerVerdict.None);

        /// <summary>Accepted AND graded — for the powers whose use can be right or wrong (role guesses, faction bets).</summary>
        public static PowerOutcome Accept(IReadOnlyList<EffectDescriptor> effects, PowerVerdict verdict,
            int usesConsumed = 1)
            => new(true, effects, usesConsumed, null, verdict);

        public static PowerOutcome Reject(string reason)
            => new(false, Array.Empty<EffectDescriptor>(), 0, reason, PowerVerdict.None);

        /// <summary>Accepted, consumes a use, but produces no effect (e.g. a no-target passive that still "ran").</summary>
        public static PowerOutcome AcceptEmpty(int usesConsumed = 1)
            => new(true, Array.Empty<EffectDescriptor>(), usesConsumed, null, PowerVerdict.None);
    }
}
