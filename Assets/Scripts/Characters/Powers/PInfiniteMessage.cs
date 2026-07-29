using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;

namespace Characters.Powers
{
    public class PInfiniteMessage : Power
    {
        // Powers-POCO v2: InfiniteMessageDecision (pure) — set the owner's messageLeft to int.MaxValue when
        // the power is reparented. The reparent trigger (an engine event) stays here; RunDecisionEffects is
        // server-guarded. Behaviour-identical to the old inline write.
        private readonly InfiniteMessageDecision _decision = new();

        protected override void Awake()
        {
            base.Awake();
            onPowerReparented += OnPowerReparented;
        }

        private void OnPowerReparented()
        {
            RunDecisionEffects(_decision, new PowerContext(ownerSlot: (int)ownerClientId.Value));
        }
    }
}