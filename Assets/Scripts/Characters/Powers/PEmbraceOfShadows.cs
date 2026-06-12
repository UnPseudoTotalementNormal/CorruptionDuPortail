#region

using System;
using Characters.Powers.Interfaces;
using Characters.Powers.Target;
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
    public class PEmbraceOfShadows : Power, IFailablePower
    {
        public EventReference onCorruptionSuccessfulSound;
        public EventReference onCorruptionFailedSound;
        public event Action onPowerSuccessful;
        public event Action onPowerFailed;

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
            roleTargetSystem.NewTargeting(ownerClientId.Value, _character.ownerClientId.Value);
            if (_character.role.IsTheSameRole(_role))
            {
                _character.CorruptPlayerServerRpc();
                InvokeOnCharacterCorruptedRpc(_character.ownerClientId.Value);
                gameInfoRevealer.SetRevealLevel(
                    _character.ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal, ownerClientId.Value);
                gameInfoRevealer.SetRevealLevel(
                    _character.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, ownerClientId.Value);
                onCorruptionSuccessfulSound.TryPlayOneShot();
            }
            else
            {
                InvokeOnCharacterCorruptionFailedRpc(_character.ownerClientId.Value);
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
        
        private void OnCorruptionSuccessful() //TODO : THIS
        {
            
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
            SelectionFlowService.instance.StartCharacterThenRoleSelection(targetValidator, OnCharacterAndRolePicked,
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
            SelectionFlowService.instance.CancelSelection();
        }
    }
}
