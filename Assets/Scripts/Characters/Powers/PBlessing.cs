#region

using System;
using System.Collections.Generic;
using UnityEngine;
using Characters.Powers.Target;
using ChatSystem;
using GameLogic;
using GameLogic.GameStates;
using Network;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine.Assertions;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PBlessing : Power
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

            TryBlessCharacterServerRpc(_character.ownerClientId.Value, _role);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void TryBlessCharacterServerRpc(ulong _blessingCharacterId, Role _compareRole)
        {
            Character _blessingCharacter = characterManager.GetCharacter(_blessingCharacterId, false);
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _blessingCharacterId);
            
            if (_blessingCharacter.role.IsTheSameRole(_compareRole))
            {
                if (!_blessingCharacter.isHealed.Value)
                {
                    _blessingCharacter.HealPlayerServerRpc();
                }
                gameInfoRevealer.SendRevealLevelRpc(
                    _blessingCharacter.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, ownerClientId.Value, true);
                _blessingCharacter.isBlessed.Value = true;
                
                chatManager.ReceiveChatMessageRpc(new ChatMessage(
                    GameValues.FAKE_CLIENT_ID,
                    $"{LobbyPlayerInfoHolder.instance.GetPlayerInfo(_blessingCharacterId).playerName} est maintenant béni.",
                    (int)ChatWindowIDs.Server),
                    characterManager.GetSafeRpcTarget(ownerClientId.Value));
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
