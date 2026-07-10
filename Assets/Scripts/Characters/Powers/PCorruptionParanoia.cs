using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;

namespace Characters.Powers
{
    public class PCorruptionParanoia : Power
    {
        // Powers-POCO v2: the whole decision lives in CorruptionParanoiaDecision (pure, EditMode-tested).
        // This NetworkBehaviour is now a humble host — it triggers the decision on game start and lets the
        // executor registry realise the reveal. Behaviour-identical to the old inline SendRevealLevelRpc.
        private readonly CorruptionParanoiaDecision _decision = new();

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            RunDecisionEffects(_decision, new PowerContext(ownerSlot: (int)ownerClientId.Value));
        }
    }
}