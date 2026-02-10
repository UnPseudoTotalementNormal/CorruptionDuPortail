#region

using System;
using System.Collections.Generic;
using System.Linq;
using Board.UI.CharacterBar;
using Characters.Powers.Target;
using ChatSystem;
using FocusSystem;
using GameLogic;
using RoleTarget;
using Unity.Netcode;
using UnityEngine.Assertions;
using FocusType = FocusSystem.FocusType;
using Board;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PVisionOfTheImpossible : Power
    {
        public int charactersToSelect = 2;
        public int rolesToSelect = 2;
        
        [NonSerialized] private List<Character> clickedCharacters = new();
        [NonSerialized] private List<Role> clickedRoles = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
            targetValidator.AddRule(ctx => 
            {
                // Pour les personnages: ne pas inclure ceux déjà cliqués
                if (ctx.targetType == TargetUtils.TargetType.Character)
                {
                    return !clickedCharacters.Any(c => c.ownerClientId.Value == ctx.targetId);
                }
                // Pour les rôles: ne pas inclure ceux déjà cliqués
                else
                {
                    var character = GameManager.instance.characterManager.GetCharacter(ctx.targetId, false);
                    return character == null || !clickedRoles.Any(_r => _r.IsTheSameRole(character.role));
                }
            });
        }
        
        private void OnCardClicked(Card _cardClicked)
        {
            var _clickedCharacter = _cardClicked.characterInfo;
            if (!CheckIsTargetValid(_clickedCharacter.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            clickedCharacters.Add(_clickedCharacter);
            var _clickedCard = BoardManager.instance.visibleCards.First(_card =>
                _card.characterInfo.ownerClientId.Value == _clickedCharacter.ownerClientId.Value);
            FocusManager.instance.UnfocusObject(_clickedCard.gameObject);
            if (clickedCharacters.Count >= charactersToSelect)
            {
                BoardManager.instance.onCardClicked -= OnCardClicked;
                GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
                FocusManager.instance.SetFocusOnType(FocusType.Roles, id => CheckIsTargetValid(id, TargetUtils.TargetType.Role));
            }
        }

        private void OnCharacterBarClicked(Character _characterClicked)
        {
            if (!CheckIsTargetValid(_characterClicked.ownerClientId.Value, TargetUtils.TargetType.Role))
            {
                return;
            }
            clickedRoles.Add(_characterClicked.role);
            List<CharactersBarObject> _characterBarObjects = GameManager.instance.charactersBar.GetCharacterBarObject(_characterClicked.role);
            foreach (var _characterBarObject in _characterBarObjects)
            {
                FocusManager.instance.UnfocusObject(_characterBarObject.gameObject);
            }
            if (clickedRoles.Count >= rolesToSelect)
            {
                GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
                FocusManager.instance.UnfocusAll();
                
                OnUsed();
                
                OnVisionGuessServerRpc(
                    clickedCharacters.Select(_c => _c.ownerClientId.Value).ToArray(),
                    clickedRoles.ToArray());
            }
        }

        [Rpc(SendTo.Server)]
        private void OnVisionGuessServerRpc(ulong[] _guessedCharacterIds, Role[] _guessedRoles)
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "OnVisionGuessServerRpc should only be called on server");
            string _message = string.Empty;
            foreach (var _guessedCharacterId in _guessedCharacterIds)
            {
                var _guessedCharacter = GameManager.instance.characterManager.GetCharacter(_guessedCharacterId, false);
                RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _guessedCharacter.ownerClientId.Value);
                if (_guessedRoles.Any(_r => _r.IsTheSameRole(_guessedCharacter.role)))
                {
                    if (_message != String.Empty)
                    {
                        _message += "\n";
                    }
                    _message += $"{_guessedCharacter.GetOwnerPseudo()} est l'un de ces personnages.";
                    break;
                }
            }
            if (_message == String.Empty)
            {
                _message = "Aucun personnage n'a été trouvé.";
            }
            ChatManager.instance.ReceiveChatMessageRpc(new ChatMessage(GameValues.FAKE_CLIENT_ID, _message, (int)ChatWindowIDs.Server),
                _rpcParams:GameManager.instance.RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
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