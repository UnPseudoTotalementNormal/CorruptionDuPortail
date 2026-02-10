#region

using System;
using System.Collections.Generic;
using Characters.Powers.Target;
using ChatSystem;
using FocusSystem;
using GameLogic;
using GameLogic.GameStates;
using Network;
using RoleTarget;
using Unity.Netcode;
using UnityEngine.Assertions;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PBlessing : Power
    {
        [NonSerialized] private Character clickedCharacter;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }
        
        private void OnCardClicked(Card _clickedCard)
        {
            clickedCharacter = _clickedCard.characterInfo;

            if (!CheckIsTargetValid(clickedCharacter.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Roles, id => CheckIsTargetValid(id, TargetUtils.TargetType.Role));
            FocusManager.instance.FocusObject(_clickedCard.gameObject);
        }
        
        private void OnCharacterBarClicked(Character _character)
        {
            if (!CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Role))
            {
                return;
            }
            
            TryBlessCharacterServerRpc(clickedCharacter.ownerClientId.Value, _character.role);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void TryBlessCharacterServerRpc(ulong _blessingCharacterId, Role _compareRole)
        {
            Character _blessingCharacter = GameManager.instance.characterManager.GetCharacter(_blessingCharacterId, false);
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _blessingCharacterId);
            
            if (_blessingCharacter.role.IsTheSameRole(_compareRole))
            {
                if (!_blessingCharacter.isHealed.Value)
                {
                    _blessingCharacter.HealPlayerServerRpc();
                }
                GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(
                    _blessingCharacter.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, true,
                    NetworkManager.RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
                _blessingCharacter.isBlessed.Value = true;
                
                ChatManager.instance.ReceiveChatMessageRpc(new ChatMessage(
                    GameValues.FAKE_CLIENT_ID,
                    $"{LobbyPlayerInfoHolder.instance.GetPlayerInfo(_blessingCharacterId).playerName} est maintenant béni.",
                    (int)ChatWindowIDs.Server),
                    NetworkManager.RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
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

        public override void StartUse()
        {
            base.StartUse();
            BoardManager.instance.onCardClicked += OnCardClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards, id => CheckIsTargetValid(id, TargetUtils.TargetType.Character));

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