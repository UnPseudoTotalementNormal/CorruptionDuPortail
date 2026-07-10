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
        // The corruption reveals (RevealInfo Broadcast:false → SetRevealLevel) are OWNER-LOCAL writes; the
        // authoritative mutations self-RPC to the server (CorruptPlayerServerRpc, NewTargetingRpc) and the
        // success/failure events fan out via SendTo.Everyone RPCs. The whole decision therefore runs on the
        // owner's client — exactly as v1 did. An earlier v2 pass forwarded the picks through a SendTo.Server
        // RPC, which made the reveals land on the host instead of a remote caster's board.
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

            // Runs on the owner's client (selection callback). Owner-local reveals apply here directly;
            // the authoritative corrupt/targeting effects self-RPC to the server. See the class-level note.
            RunClientDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value,
                targetSlot: (int)_character.ownerClientId.Value,
                secondaryTargetSlot: (int)_role.ownerClientId,
                roster: Roster), SelfState);

            // Local audio cue for the picker — computed client-side from the two picks, unchanged from v1.
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
