#region

using System;
using UnityEngine;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using GameLogic;
using Network;
using UI.BoardUI.Selection;
using Unity.Netcode;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PHighPriorityBounty : Power
    {
        public RoleID targetRoleID = RoleID.Robot;

        // Powers-POCO v2: server logic in HighPriorityBountyDecision (pure): self-target, then if the target
        // is the robot eliminate + broadcast + public-reveal, else chain the owner + warn privately. The
        // client-side NewTargeting(owner->target) in OnCharacterPicked is unchanged. Behaviour-identical.
        private readonly HighPriorityBountyDecision _decision = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }
        
        private void OnCharacterPicked(Character _character)
        {
            if (!CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            roleTargetSystem.NewTargeting(ownerClientId.Value, _character.ownerClientId.Value);
            OnCardClickedServerRpc(_character.ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void OnCardClickedServerRpc(ulong _targetClientId, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _targetClientId)) return; // NET-09: server-side use authorization
            OnCardClickedRpc(_targetClientId);
        }
        
        private void OnCardClickedRpc(ulong _targetClientId)
        {
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value, targetSlot: (int)_targetClientId, roster: Roster));
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

        [SerializeField] private string pickerDescription;

        public override void StartUse()
        {
            base.StartUse();
            selectionFlowService.StartCharacterSelection(targetValidator, OnCharacterPicked,
                new SelectionFlowOptions { stepDescriptions = new[] { pickerDescription } });
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