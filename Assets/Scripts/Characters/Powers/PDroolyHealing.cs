#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Characters.Powers.Target;
using ChatSystem;
using Extensions;
using FMODUnity;
using FocusSystem;
using GameLogic;
using GameLogic.GameStates;
using Network;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;
using FocusType = FocusSystem.FocusType;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PDroolyHealing : Power
    {
        [NonSerialized] private Character clickedCharacter;
        public EventReference onHealSuccessfulSound;
        public EventReference onHealFailedSound;

        public List<ulong> healedCharactersThisNight = new();

        private void OnCardClicked(Card _clickedCard)
        {
            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(_clickedCard.characterInfo.ownerClientId.Value))
            {
                return;
            }
            clickedCharacter = _clickedCard.characterInfo;
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            FocusManager.instance.SetFocusOnType(FocusType.Roles, targetIncludeFlags);
            FocusManager.instance.FocusObject(_clickedCard.gameObject);
        }
        private void OnCharacterBarClicked(Character _character)
        {
            if (!TargetUtils.GetTargetsForRoles(targetIncludeFlags).Contains(_character.ownerClientId.Value))
            {
                return;
            }
            var _senderId = NetworkManager.Singleton.LocalClientId;
            TryHealServerRpc(clickedCharacter.ownerClientId.Value, _character.role);
            OnUsed();
        }
        [Rpc(SendTo.Server)]
        private void TryHealServerRpc(ulong _healingCharacterId, Role _compareRole)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _healingCharacterId);
            PDroolyHealing _power = (PDroolyHealing)GameManager.instance.characterManager.GetCharacter(ownerClientId.Value).role.powers.First(_p => _p.GetType() == typeof(PDroolyHealing));
            var _choosedCharacter = GameManager.instance.characterManager.GetCharacter(_healingCharacterId, false);
            bool _healSuccess = false;
            if (_compareRole.IsTheSameRole(_choosedCharacter.role))
            {
                if (_choosedCharacter.isCorrupted.Value || _choosedCharacter.isHealed.Value)
                {
                    _healSuccess = true;
                    _choosedCharacter.HealPlayerServerRpc();
                    GameManager.instance.characterManager.AskForUpdateAllCharactersRpc();
                }
                healedCharactersThisNight.Add(_healingCharacterId);
                OnHealSuccessfulRpc(_choosedCharacter.ownerClientId.Value, NetworkManager.RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
            }
            GameAudioManager.instance.PlayOneShotRpc(
                _healSuccess ? onHealSuccessfulSound.GetPath() : onHealFailedSound.GetPath(),
                NetworkManager.Singleton.RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
        }
        [Rpc(SendTo.SpecifiedInParams)]
        private void OnHealSuccessfulRpc(ulong _targetClientId, RpcParams _rpcParams = default)
        {
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _targetClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal);
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            var _gameManager = GameManager.instance;
            foreach (var _awakeningState in _gameManager.GetGameStates(typeof(AwakeningState)))
            {
                _awakeningState.onStateEndServer += OnNightEndedServer;
            }
        }

        private void OnNightEndedServer()
        {
            foreach (ulong _healedCharacterId in healedCharactersThisNight)
            {
                Character _healedCharacter = GameManager.instance.characterManager.GetCharacter(_healedCharacterId, false);
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
                ChatManager.instance.ReceiveChatMessageRpc(_chatMessage, RpcTarget.Everyone);
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

        public override void StartUse()
        {
            base.StartUse();
            BoardManager.instance.onCardClicked += OnCardClicked;

            FocusManager.instance.SetFocusOnType(FocusType.Cards, targetIncludeFlags);

            clickedCharacter = null;
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
            BoardManager.instance.onCardClicked -= OnCardClicked;
            GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
            FocusManager.instance.UnfocusAll();
        }
    }
}