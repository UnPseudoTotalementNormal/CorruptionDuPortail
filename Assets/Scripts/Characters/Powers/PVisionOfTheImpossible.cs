#region

using System;
using System.Collections.Generic;
using System.Linq;
using Board.UI.CharacterBar;
using Characters.Powers.Target;
using ChatSystem;
using FocusSystem;
using GameLogic;
using Network;
using RoleTarget;
using Unity.Netcode;
using UnityEngine.Assertions;
using FocusType = FocusSystem.FocusType;

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
        
        private void OnCardClicked(Card _cardClicked)
        {
            var _clickedCharacter = _cardClicked.characterInfo;
            
            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(_clickedCharacter.ownerClientId.Value))
            {
                return;
            }
            
            if (clickedCharacters.Contains(_clickedCharacter))
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
                FocusManager.instance.SetFocusOnType(FocusType.Roles, targetIncludeFlags);
            }
        }

        private void OnCharacterBarClicked(Character _characterClicked)
        {
            if (!TargetUtils.GetTargetsForRoles(targetIncludeFlags).Contains(_characterClicked.ownerClientId.Value))
            {
                return;
            }
            
            if (clickedRoles.Any(_r => _r.IsTheSameRole(_characterClicked.role)))
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
                
                GameManager.instance.DoPowerStaticMethodRpc(GetType().FullName, nameof(OnVisionGuessServerRpc),
                    new[] {  
                        new NetworkSerializableObject(NetworkManager.Singleton.LocalClientId),
                        new NetworkSerializableObject(clickedCharacters.Select(c => c.ownerClientId.Value).ToArray()),
                        new NetworkSerializableObject(clickedRoles.ToArray())
                    }, 
                    new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
            }
        }
        
        private static void OnVisionGuessServerRpc(ulong _sender, ulong[] _guessedCharacterIds, Role[] _guessedRoles)
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "OnVisionGuessServerRpc should only be called on server");
            
            string _message = string.Empty;
            foreach (var _guessedCharacterId in _guessedCharacterIds)
            {
                var _guessedCharacter = GameManager.instance.characterManager.GetCharacter(_guessedCharacterId, false);
                RoleTargetSystem.instance.NewTargeting(_sender, _guessedCharacter.ownerClientId.Value);
                
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
                _rpcParams:GameManager.instance.RpcTarget.Single(_sender, RpcTargetUse.Persistent));
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
        }

        public override void OnUsed()
        {
            base.OnUsed();
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
        }

        public override object Clone()
        {
            var _clonedPower = (PVisionOfTheImpossible)base.Clone();
            _clonedPower.clickedCharacters = clickedCharacters.ToList();
            _clonedPower.clickedRoles = clickedRoles.ToList();
            return _clonedPower;
        }
    }
}