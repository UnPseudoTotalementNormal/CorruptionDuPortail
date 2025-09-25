#region

using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.GameStates;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine.Assertions;

#endregion

namespace GameLogic
{
    public class GameInfoRevealer : NetworkBehaviour
    {
        public Dictionary<ulong, CharacterInfoReveal> charactersInfoRevealed = new();

        public void Start()
        {
            GameManager.instance.GetGameStates(typeof(RoleAttributionState)).First().onStateEndClient += OnRolesAttributed;
        }

        private void OnRolesAttributed()
        {
            charactersInfoRevealed = new Dictionary<ulong, CharacterInfoReveal>();
            foreach (var _character in GameManager.instance.characterManager.GetCharacters())
            {
                var _characterInfoReveal = new CharacterInfoReveal();
                if (_character.ownerClientId.Value == NetworkManager.LocalClientId)
                {
                    _characterInfoReveal.isRoleRevealed = RevealLevel.Personal;
                }
                charactersInfoRevealed.Add(_character.ownerClientId.Value, _characterInfoReveal);
            }
        }

        public CharacterInfoReveal GetCharacterInfo(ulong _clientId)
        {
            return charactersInfoRevealed[_clientId];
        }
        
        public void SetRevealLevel(ulong _clientId, FixedString64Bytes _revealVariableName, RevealLevel _revealLevel, bool _showInfo = true)
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
                    if (!_showInfo)
                    {
                        return;
                    }
                    _ = BoardManager.instance.visibleCards.Find(_card => _card.characterInfo.ownerClientId.Value == _clientId)
                        .ShowPseudoWithRevealedInfo(true);
                    break;
            }
        }

        [Rpc(SendTo.Everyone, AllowTargetOverride = true)]
        public void SetRevealLevelRpc(ulong _clientId, FixedString64Bytes _revealVariableName, RevealLevel _revealLevel, bool _showInfo = true, RpcParams _rpcParams = default)
        {
            SetRevealLevel(_clientId, _revealVariableName, _revealLevel, _showInfo);
        }
    }

    [Serializable]
    public class CharacterInfoReveal
    {
        public RevealLevel isRoleRevealed = RevealLevel.False;
        public RevealLevel isCorruptRevealed = RevealLevel.False;
        public RevealLevel forceCorruptOnRoleRevealed = RevealLevel.False;
    }
    
    public enum RevealLevel
    {
        False = 0,
        Personal = 10,
        Public = 20,
    }
}
