#region

using System;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PCorruptionInsight : Power
    {
        // Powers-POCO v2: the whole logic lives in CorruptionInsightDecision (pure, EditMode-tested) — on
        // game start, reveal every roster character's corruption to the owner. This host just triggers the
        // decision with the live roster and dispatches. Behaviour-identical to the old inline reveal loop.
        private readonly CorruptionInsightDecision _decision = new();

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            RunDecisionEffects(_decision,
                new PowerContext(ownerSlot: (int)ownerClientId.Value, roster: Roster));
        }
    }
}