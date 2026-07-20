using System;
using System.Collections.Generic;
using System.Linq;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers
{
    [Serializable]
    public class PClandestineObservation : Power
    {
        // Lot D: active, multi-target. The owner picks N players (N = the non-chosen count of the STARTING
        // composition) and learns how many élus are among them. The count logic is ClandestineObservationDecision
        // (pure); the multi-pick selection flow + N capture stay here.
        private readonly ClandestineObservationDecision _decision = new();

        // N = non-chosen (anomaly + marginal) count captured once at game start — fixed all game (design D2).
        // Replicated so the owner's client knows how many players to pick.
        private readonly NetworkVariable<int> _nonEluCount = new();

        [SerializeField] private string[] pickerStepDescriptions;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
            // C3: the Chasseuse cannot target herself.
            targetValidator.AddRule(ctx => ctx.targetId != ownerClientId.Value);
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            if (IsServer)
            {
                _nonEluCount.Value = characterManager.GetCharacters(false)
                    .Count(_c => _c.role.factionType != FactionType.chosen);
            }
        }

        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            return base.CanUse(_ignoreCurrentlyUsed);
        }

        public override void StartUse()
        {
            base.StartUse();
            // N is the STARTING non-chosen count (design D2). Chaining/elimination can shrink the valid pool
            // below N mid-game; without clamping, the picker would demand more distinct valid targets than
            // exist and never complete (a stuck picker + a wasted use). Clamp to what is actually targetable so
            // the power stays usable. The "always exactly N from the start" intent is a design point flagged
            // for revisit — the Chasseuse rework is not frozen.
            int _available = GetValidTargets().Count;
            int _pickCount = System.Math.Min(_nonEluCount.Value, _available);
            selectionFlowService.StartMultiCharacterSelection(targetValidator, _pickCount,
                OnAllCharactersPicked, new SelectionFlowOptions { stepDescriptions = pickerStepDescriptions });
        }

        private void OnAllCharactersPicked(List<Character> _characters)
        {
            ulong[] _ids = _characters.Where(_c => _c != null).Select(_c => _c.ownerClientId.Value).ToArray();
            ObserveServerRpc(_ids);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void ObserveServerRpc(ulong[] _targetIds)
        {
            var _slots = _targetIds.Select(_id => (int)_id).ToList();
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value, roster: Roster, targetSlots: _slots));
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
            selectionFlowService.CancelSelection();
        }
    }
}
