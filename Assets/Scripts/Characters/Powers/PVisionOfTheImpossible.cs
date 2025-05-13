using System;
using System.Collections.Generic;
using System.Linq;
using Board.UI.CharacterBar;
using ChatSystem;
using FocusSystem;
using GameLogic;
using Network;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;
using FocusType = FocusSystem.FocusType;

namespace Characters.Powers
{
    [Serializable]
    public class PVisionOfTheImpossible : Power
    {
        public int charactersToSelect = 2;
        public int rolesToSelect = 2;
        
        [NonSerialized] private List<Character> clickedCharacters = new();
        [NonSerialized] private List<Role> clickedRoles = new();
        
        [NonSerialized] private List<Character> ignoreCharacters = new();
        
        private void OnCardClicked(Card _cardClicked)
        {
            var _clickedCharacter = _cardClicked.characterInfo;
            if (clickedCharacters.Contains(_clickedCharacter) || ignoreCharacters.Contains(_clickedCharacter))
            {
                return;
            }
            
            clickedCharacters.Add(_clickedCharacter);

            var _clickedCard = BoardManager.instance.visibleCards.First(_card =>
                _card.characterInfo.ownerClientId == _clickedCharacter.ownerClientId);
            FocusManager.instance.UnfocusObject(_clickedCard.gameObject);

            if (clickedCharacters.Count >= charactersToSelect)
            {
                BoardManager.instance.onCardClicked -= OnCardClicked;
                GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
                FocusManager.instance.SetFocusOnType(FocusType.Roles, true);
                
                foreach (var _ignoreRole in GetIgnoreRoles())
                {
                    List<CharactersBarObject> _characterBarObjects = GameManager.instance.charactersBar.GetCharacterBarObject(_ignoreRole);
                    var _focusedObject = _characterBarObjects.FirstOrDefault(_c => _c.playerCharacter.ownerClientId == _ignoreRole.ownerClientId);
                    if (_focusedObject)
                    {
                        FocusManager.instance.UnfocusObject(_focusedObject.gameObject);
                    }
                }
            }
        }

        private void OnCharacterBarClicked(Character _characterClicked)
        {
            if (GetIgnoreRoles().Any(_r => _r.IsTheSameRole(_characterClicked.role)) || clickedRoles.Any(_r => _r.IsTheSameRole(_characterClicked.role)))
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
                        new NetworkSerializableObject(clickedCharacters.ToArray()),
                        new NetworkSerializableObject(clickedRoles.ToArray())
                    }, 
                    new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
            }
        }
        
        private static void OnVisionGuessServerRpc(ulong _sender, Character[] _guessedCharacters, Role[] _guessedRoles)
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "OnVisionGuessServerRpc should only be called on server");
            
            string _message = string.Empty;
            foreach (var _guessedCharacter in _guessedCharacters)
            {
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
            
            ChatManager.instance.SendChatMessageSingleRpc(_message, 
                GameValues.FAKE_CLIENT_ID, GameManager.instance.RpcTarget.Single(_sender, RpcTargetUse.Persistent));
        }   

        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            bool _baseValue = base.CanUse(_ignoreCurrentlyUsed);
            if (!_baseValue)
            {
                return false;
            }
            
            if (GameManager.instance.GetCharacters(false).Count - GetIgnoreCharacters().Count < charactersToSelect)
            {
                return false;
            }
            
            return true;
        }

        public override void StartUse()
        {
            base.StartUse();
            
            BoardManager.instance.onCardClicked += OnCardClicked;
            
            ignoreCharacters = GetIgnoreCharacters();
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards);
            
            foreach (var _ignoreCharacter in ignoreCharacters)
            {
                List<Card> _cards = BoardManager.instance.visibleCards;
                Card _ignoreCard = _cards.FirstOrDefault(_card => _card.characterInfo.ownerClientId == _ignoreCharacter.ownerClientId);
                if (_ignoreCard)
                {
                    FocusManager.instance.UnfocusObject(_ignoreCard.gameObject);
                }
            }
        }

        private List<Character> GetIgnoreCharacters()
        {
            var _ignoreCharacters = new List<Character>();
            foreach (var _character in GameManager.instance.GetCharacters(false))
            {
                if (GameManager.instance.gameInfoRevealer.GetCharacterInfo(_character.ownerClientId).isRoleRevealed <= RevealLevel.False)
                {
                    continue;
                }
                
                _ignoreCharacters.Add(_character);
            }

            return _ignoreCharacters;
        }

        private List<Role> GetIgnoreRoles()
        {
            var _ignoreRoles = new List<Role>();
            foreach (var _character in GameManager.instance.GetCharacters(false))
            {
                if (_character.role.factionType == FactionType.chosen)
                {
                    continue;
                }
                
                _ignoreRoles.Add(_character.role);
            }

            return _ignoreRoles;
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
            _clonedPower.ignoreCharacters = ignoreCharacters.ToList();
            _clonedPower.clickedRoles = clickedRoles.ToList();
            return _clonedPower;
        }
    }
}