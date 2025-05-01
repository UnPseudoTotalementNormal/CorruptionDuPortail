using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.GameStates;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace GameLogic
{
    public class GameInfoRevealer : NetworkBehaviour
    {
        public Dictionary<ulong, CharacterInfoReveal> charactersInfoRevealed = new();

        public void Start()
        {
            GameManager.instance.GetGameStates(typeof(RoleAttributionState)).FirstOrDefault()!.onStateEndClient += OnRolesAttributed;
        }

        private void OnRolesAttributed()
        {
            charactersInfoRevealed = new Dictionary<ulong, CharacterInfoReveal>();
            foreach (var _character in GameManager.instance.characters)
            {
                var _characterInfoReveal = new CharacterInfoReveal();
                if (_character.ownerClientId == NetworkManager.LocalClientId)
                {
                    _characterInfoReveal.isRoleRevealed = RevealLevel.Personal;
                }
                charactersInfoRevealed.Add(_character.ownerClientId, _characterInfoReveal);
            }
        }

        public CharacterInfoReveal GetCharacterInfo(ulong _clientId)
        {
            return charactersInfoRevealed[_clientId];
        }
        
        public void SetRevealLevel(ulong _clientId, FixedString64Bytes _revealVariableName, RevealLevel _revealLevel)
        {
            var _field = typeof(CharacterInfoReveal).GetField(_revealVariableName.ToString());
            Assert.IsNotNull(_field, "Field not found: " + _revealVariableName);
            
            RevealLevel _currentRevealLevel = (RevealLevel)_field.GetValue(GetCharacterInfo(_clientId));
            if ((int)_currentRevealLevel >= (int)_revealLevel)
            {
                return;
            }
            _field.SetValue(GetCharacterInfo(_clientId), _revealLevel);

            switch (_revealVariableName.ToString())
            {
                case nameof(CharacterInfoReveal.isRoleRevealed):
                    _ = BoardManager.instance.visibleCards.Find(_card => _card.characterInfo.ownerClientId == _clientId)
                        .ShowPseudoWithRevealedInfo(true);
                    break;
            }
        }

        [Rpc(SendTo.Everyone)]
        public void SetRevealLevelRpc(ulong _clientId, FixedString64Bytes _revealVariableName, RevealLevel _revealLevel)
        {
            var _field = typeof(CharacterInfoReveal).GetField(_revealVariableName.ToString());
            Assert.IsNotNull(_field, "Field not found: " + _revealVariableName);
            
            RevealLevel _currentRevealLevel = (RevealLevel)_field.GetValue(GetCharacterInfo(_clientId));
            if ((int)_currentRevealLevel > (int)_revealLevel)
            {
                return;
            }
            _field.SetValue(GetCharacterInfo(_clientId), _revealLevel);
        }
    }

    [Serializable]
    public class CharacterInfoReveal
    {
        public RevealLevel isRoleRevealed = RevealLevel.False;
        public RevealLevel isCorruptRevealed = RevealLevel.False;
    }
    
    public enum RevealLevel
    {
        False = 0,
        Personal = 10,
        Public = 20,
    }
}
