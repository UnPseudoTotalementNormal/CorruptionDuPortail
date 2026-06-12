#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Characters.Powers.Target;
using ChatSystem;
using Extensions;
using FMODUnity;
using GameLogic;
using GameLogic.GameStates;
using Network;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PDroolyHealing : Power
    {
        public EventReference onHealSuccessfulSound;
        public EventReference onHealFailedSound;

        public List<ulong> healedCharactersThisNight = new();

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
            TryHealServerRpc(_character.ownerClientId.Value, _role);
            OnUsed();
        }
        [Rpc(SendTo.Server)]
        private void TryHealServerRpc(ulong _healingCharacterId, Role _compareRole)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _healingCharacterId);
            PDroolyHealing _power = (PDroolyHealing)characterManager.GetCharacter(ownerClientId.Value).role.powers.First(_p => _p.GetType() == typeof(PDroolyHealing));
            var _choosedCharacter = characterManager.GetCharacter(_healingCharacterId, false);
            bool _healSuccess = false;
            if (_compareRole.IsTheSameRole(_choosedCharacter.role))
            {
                if (_choosedCharacter.isCorrupted.Value || _choosedCharacter.isHealed.Value)
                {
                    _healSuccess = true;
                    _choosedCharacter.HealPlayerServerRpc();
                    characterManager.AskForUpdateAllCharactersRpc();
                }
                healedCharactersThisNight.Add(_healingCharacterId);
                OnHealSuccessfulRpc(_choosedCharacter.ownerClientId.Value, characterManager.GetSafeRpcTarget(ownerClientId.Value));
            }
            GameAudioManager.instance.PlayOneShotRpc(
                _healSuccess ? onHealSuccessfulSound.GetPath() : onHealFailedSound.GetPath(),
                characterManager.GetSafeRpcTarget(ownerClientId.Value));
        }
        [Rpc(SendTo.SpecifiedInParams)]
        private void OnHealSuccessfulRpc(ulong _targetClientId, RpcParams _rpcParams = default)
        {
            gameInfoRevealer.SetRevealLevel(
                _targetClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, ownerClientId.Value);
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            var _gameManager = GameManager.For(NetworkManager);
            foreach (var _awakeningState in _gameManager.GetGameStates(typeof(AwakeningState)))
            {
                _awakeningState.onStateEndServer += OnNightEndedServer;
            }
        }

        private void OnNightEndedServer()
        {
            foreach (ulong _healedCharacterId in healedCharactersThisNight)
            {
                Character _healedCharacter = characterManager.GetCharacter(_healedCharacterId, false);
                if (!_healedCharacter)
                {
                    continue;
                }
                var _chatMessage = new ChatMessage
                {
                    message = $"{_healedCharacter.GetOwnerPseudo()} à été soigné(e) pendant la nuit avec {powerName.ToString()}",
                    senderClientId = GameValues.CHAT_SERVER_CLIENT_ID,
                    chatId = (int)ChatWindowIDs.Server
                };
                chatManager.ReceiveChatMessageRpc(_chatMessage, RpcTarget.Everyone);
            }

            healedCharactersThisNight.Clear();
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
