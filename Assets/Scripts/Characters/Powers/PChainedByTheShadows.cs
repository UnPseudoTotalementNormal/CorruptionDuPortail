#region

using System;
using UnityEngine;
using Characters.Powers.Target;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PChainedByTheShadows : Power
    {
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
            Character _corruptingCharacter = characterManager.GetCharacter(_corruptingCharacterId, false);
            roleTargetSystem.NewTargeting(ownerClientId.Value, _corruptingCharacterId);
            if (_corruptingCharacter.role.IsTheSameRole(_compareRole))
            {
                gameInfoRevealer.SendRevealLevelRpc(
                    _corruptingCharacter.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, ownerClientId.Value, true);
                if (_corruptingCharacter.role.factionType == FactionType.chosen)
                {
                    chainingManager.AddCharacterToChainingList(_corruptingCharacterId);
                }
            }
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
