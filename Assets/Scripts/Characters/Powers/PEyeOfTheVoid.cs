using System;
using ChatSystem;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;

namespace Characters.Powers
{
    [Serializable]
    public class PEyeOfTheVoid : Power
    {
        // Powers-POCO v2: EyeOfTheVoidDecision (pure) — on game start, discover the anomaly-only chat for
        // every anomaly-faction player. The chat-window id is config-as-field; on this in-place host we set
        // it in code (the prefab isn't re-authored). Behaviour-identical to the old inline discover loop.
        private readonly EyeOfTheVoidDecision _decision = new() { AnomalyChatId = (int)ChatWindowIDs.AnomalyOnly };

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            RunDecisionEffects(_decision,
                new PowerContext(ownerSlot: (int)ownerClientId.Value, roster: Roster));
        }
    }
}