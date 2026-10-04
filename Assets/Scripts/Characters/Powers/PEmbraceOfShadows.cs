#region

using System;
using Characters.Powers.Interfaces;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using Extensions;
using FMODUnity;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PEmbraceOfShadows : Power, IFailablePower, ICorruptionEvents
    {
        public EventReference onCorruptionSuccessfulSound;
        public EventReference onCorruptionFailedSound;
        public event Action onPowerSuccessful;
        public event Action onPowerFailed;

        // Powers-POCO v2 in-place wiring (Phase 3): logic lives in EmbraceOfShadowsDecision (pure, char+role
        // branch, EditMode-tested). The success/failure EVENTS are power-local (IFailablePower), reached by
        // the CorruptionSucceeded/Failed executors through SelfState via ICorruptionEvents. The faction-
        // independent success/failure SOUNDS stay adapter-side and LOCAL to the picker.
        // NET-09: the decision runs on the SERVER from server state (it used to run on the caster's client from
        // that client's replica, so a stale replica produced a real, wrong corruption). Its owner-local reveals
        // are delivered to the caster through RunDecisionEffects' localViewer, never applied on the host.
        private readonly EmbraceOfShadowsDecision _decision = new();

        void ICorruptionEvents.RaiseSucceeded(int _slot) => InvokeOnCharacterCorruptedRpc((ulong)_slot);
        void ICorruptionEvents.RaiseFailed(int _slot) => InvokeOnCharacterCorruptionFailedRpc((ulong)_slot);

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

            EmbraceServerRpc(_character.ownerClientId.Value, _role.ownerClientId);

            // Local audio cue for the picker (cosmetic) — computed from the two picks, unchanged from v1.
            if (_character.role.IsTheSameRole(_role))
            {
                onCorruptionSuccessfulSound.TryPlayOneShot();
            }
            else
            {
                onCorruptionFailedSound.TryPlayOneShot();
            }
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void EmbraceServerRpc(ulong _targetCharacterId, ulong _pickedRoleOwnerId, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _targetCharacterId, _pickedRoleOwnerId)) return; // NET-09

            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value,
                targetSlot: (int)_targetCharacterId,
                secondaryTargetSlot: (int)_pickedRoleOwnerId,
                roster: Roster), SelfState, localViewer: ownerClientId.Value);
        }

        [Rpc(SendTo.Everyone)]
        private void InvokeOnCharacterCorruptedRpc(ulong characterId)
        {
            onPowerSuccessful?.Invoke();
        }

        [Rpc(SendTo.Everyone)]
        private void InvokeOnCharacterCorruptionFailedRpc(ulong characterId)
        {
            onPowerFailed?.Invoke();
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
    }
}
