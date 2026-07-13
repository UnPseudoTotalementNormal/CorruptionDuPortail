using System;
using UnityEngine;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;

namespace Characters.Powers
{
    [Serializable]
    public class POmniscience : Power, IHackTargetState //TODO: rework win condition to use power instead of creating a wincondition
    {
        public ulong hackedCharacterClientId = HACKED_CHARACTER_DEFAULT;
        public const ulong HACKED_CHARACTER_DEFAULT = 4994996541621;

        // Powers-POCO v2: server logic lives in OmniscienceDecision (pure). The hacked-target write is
        // power-local state, exposed through IHackTargetState and reached by StoreHackTargetExecutor via
        // SelfState. Behaviour-identical to the old resolver path (store target + reveal role + refresh).
        private readonly OmniscienceDecision _decision = new();

        void IHackTargetState.StoreHackTarget(int slot) => hackedCharacterClientId = (ulong)slot;

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
        private void OnCardClickedServerRpc(ulong _targetClientId)
        {
            OnCardClickedRpc(_targetClientId);
        }
        private void OnCardClickedRpc(ulong _targetClientId)
        {
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value, targetSlot: (int)_targetClientId, roster: Roster), SelfState);
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