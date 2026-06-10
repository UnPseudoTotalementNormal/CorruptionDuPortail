using CorruptionDuPortail.Domain;

namespace Characters.Powers
{
    /// <summary>
    /// Story 4.0 observation seam. Powers emit their effect INTENTIONS as
    /// <see cref="EffectDescriptor"/> bricks alongside the (untouched) verbatim effect calls,
    /// via <see cref="PowerEffectTrace.Record"/>. In production the active observer is a
    /// no-op (<see cref="NullObserver"/>) so there is ZERO behavior change — the real effects
    /// still run through the original singleton/RPC calls. The global golden test (Story 4.0)
    /// swaps in a recording observer to capture the ordered brick list as the standing oracle,
    /// and the per-power stories 4.1–4.4 convert each power from "inline effect + adjacent
    /// Record" to "PowerResolver returns the list + adapter dispatch switch" — proven against
    /// this oracle.
    /// </summary>
    public interface IPowerEffectObserver
    {
        void Record(EffectDescriptor effect);
    }

    /// <summary>
    /// Ambient, swappable trace hook for power effect intentions. Default observer is a no-op;
    /// tests assign a recorder and MUST reset it in teardown.
    /// </summary>
    public static class PowerEffectTrace
    {
        public static IPowerEffectObserver Observer = NullObserver.Instance;

        public static void Record(EffectDescriptor effect) => Observer.Record(effect);

        /// <summary>Restore the production no-op observer (call from test teardown).</summary>
        public static void Reset() => Observer = NullObserver.Instance;

        private sealed class NullObserver : IPowerEffectObserver
        {
            public static readonly NullObserver Instance = new();
            public void Record(EffectDescriptor effect) { }
        }
    }
}
