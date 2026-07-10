#region

using System;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PAutoCorruption : Power
    {
        // Powers-POCO v2: logic in AutoCorruptionDecision (pure) — corrupt the owner at game start.
        private readonly AutoCorruptionDecision _decision = new();

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            RunDecisionEffects(_decision, new PowerContext(ownerSlot: (int)ownerClientId.Value));
        }
    }
}