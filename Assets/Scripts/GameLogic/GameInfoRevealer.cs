using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

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

        public CharacterInfoReveal GetCharacterInfo(ulong _characterInfoOwnerClientId)
        {
            return charactersInfoRevealed[_characterInfoOwnerClientId];
        }
    }

    [Serializable]
    public class CharacterInfoReveal
    {
        public RevealLevel isRoleRevealed = RevealLevel.False;
    }
    
    public enum RevealLevel
    {
        False = 0,
        Personal = 10,
        Public = 20,
    }
}
