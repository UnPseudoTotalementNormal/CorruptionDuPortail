#region

using System;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PCorruptionKnowledge : Power
    {
        // Powers-POCO v2: CorruptionKnowledgeDecision (pure) — reveal every roster character's
        // forceCorruptOnRoleRevealed flag to the owner at game start. Same shape as CorruptionInsight.
        private readonly CorruptionKnowledgeDecision _decision = new();

        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            bool _baseValue = base.CanUse(_ignoreCurrentlyUsed);
            if (!_baseValue)
            {
                return false;
            }
            return true;
        }

        public override void StartUse()
        {
            base.StartUse();
        }

        public override void Cancel()
        {
            if (!isCurrentlyUsed)
            {
                return;
            }
            base.Cancel();
        }
        
        protected override void StopUse()
        {
            base.StopUse();
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            RunDecisionEffects(_decision,
                new PowerContext(ownerSlot: (int)ownerClientId.Value, roster: Roster));
        }
        private void OnGameStartedClient()
        {
        }
    }
}
