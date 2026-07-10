#region

using System;
using UnityEngine;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PChainedByTheShadows : Power
    {
        // Powers-POCO v2: server logic in ChainedByShadowsDecision (pure). The char+role selection flow
        // stays here; the RPC body triggers the decision. The picked role's owner is the secondary slot the
        // decision compares roles against. Behaviour-identical to the old inline reveal+chain.
        private readonly ChainedByShadowsDecision _decision = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }

        private void OnCharacterAndRolePicked(Character _character, Role _role)
        {
            if (!_character ||
                !CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Character) ||
                !CheckIsTargetValid(_role.ownerClientId, TargetUtils.TargetType.Role))
            {
                return;
            }
            TryCorruptCharacterServerRpc(_character.ownerClientId.Value, _role);
            OnUsed();
        }
        [Rpc(SendTo.Server)]
        private void TryCorruptCharacterServerRpc(ulong _corruptingCharacterId, Role _compareRole)
        {
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value,
                targetSlot: (int)_corruptingCharacterId,
                secondaryTargetSlot: (int)_compareRole.ownerClientId,
                roster: Roster));
        }

        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            bool _baseValue = base.CanUse(_ignoreCurrentlyUsed);
            if (!_baseValue)
            {
                return false;
            }
            return true;
        }

        [SerializeField] private string[] pickerStepDescriptions;

        public override void StartUse()
        {
            base.StartUse();
            selectionFlowService.StartCharacterThenRoleSelection(targetValidator, OnCharacterAndRolePicked,
                new SelectionFlowOptions { stepDescriptions = pickerStepDescriptions });
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

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
        }
    }
}
