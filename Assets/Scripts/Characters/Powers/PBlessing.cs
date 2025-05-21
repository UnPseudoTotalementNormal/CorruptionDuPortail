#region

using System;
using System.Collections.Generic;
using System.Linq;
using Board.UI.CharacterBar;
using ChatSystem;
using FocusSystem;
using GameLogic;
using GameLogic.GameStates;
using Network;
using Unity.Netcode;
using UnityEngine.Assertions;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PBlessing : Power
    {
        [NonSerialized] private bool isSubscribedToMornings = false;
        [NonSerialized] private static List<ulong> blessingCharacterIdOnMorning = new();
        
        [NonSerialized] private Character clickedCharacter;
        
        private void OnCardClicked(Card _clickedCard)
        {
            clickedCharacter = _clickedCard.characterInfo;

            if (GetIgnoreCharacters().Any(_c => _c.ownerClientId == clickedCharacter.ownerClientId))
            {
                return;
            }
            
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Roles);
            FocusManager.instance.FocusObject(_clickedCard.gameObject);
            
            foreach (var _ignoreRole in GetIgnoreRoles())
            {
                List<CharactersBarObject> _characterBarObjects = GameManager.instance.charactersBar.charactersBarObjects;
                List<CharactersBarObject> _ignoreObjects = _characterBarObjects.FindAll(_c => _c.playerCharacter.role.IsTheSameRole(_ignoreRole));
                foreach (var _ignoreObject in _ignoreObjects)
                {
                    FocusManager.instance.UnfocusObject(_ignoreObject.gameObject);
                }
            }
        }
        
        private void OnCharacterBarClicked(Character _character)
        {
            var _roleClicked = _character.role;

            if (GetIgnoreRoles().Any(_r => _r.IsTheSameRole(_roleClicked)))
            {
                return;
            }
            
            GameManager.instance.DoPowerStaticMethodRpc(GetType().FullName, nameof(TryBlessCharacterServerRpc),
                new[] {  
                    new NetworkSerializableObject(NetworkManager.Singleton.LocalClientId),
                    new NetworkSerializableObject(clickedCharacter.ownerClientId),
                    new NetworkSerializableObject(_character.role)
                }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
            OnUsed();
        }

        private static void TryBlessCharacterServerRpc(ulong _sender, ulong _blessingCharacterId, Role _compareRole)
        {
            Character _blessingCharacter = GameManager.instance.GetCharacter(_blessingCharacterId, false);
            if (_blessingCharacter.role.IsTheSameRole(_compareRole))
            {
                GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(
                    _blessingCharacter.ownerClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal,
                    GameManager.instance.RpcTarget.Single(_sender, RpcTargetUse.Persistent));
                blessingCharacterIdOnMorning.Add(_blessingCharacterId);
                
            }
        }

        private static void OnMorningBlessingServer()
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "OnMorningBlessingServer should only be called on server");
            foreach (var _characterId in blessingCharacterIdOnMorning)
            {
                GameManager.instance.GetCharacter(_characterId).isBlessed = true;
                var _playerName = LobbyPlayerInfoHolder.instance.GetPlayerInfo(_characterId).playerName;
                ChatManager.instance.SendChatMessageServerRpc($"{_playerName} a été béni. (ne fais absolument rien pour l'instant)", GameValues.FAKE_CLIENT_ID); //TODO: Jarvis, faudra faire ça
            }
            blessingCharacterIdOnMorning.Clear();
            GameManager.instance.AskForUpdateAllCharactersRpc();
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
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards);
            
            foreach (var _ignoreCharacter in GetIgnoreCharacters())
            {
                var _ignoreCard = BoardManager.instance.visibleCards.Find(_card => _card.characterInfo.ownerClientId == _ignoreCharacter.ownerClientId);
                if (_ignoreCard)
                {
                    FocusManager.instance.UnfocusObject(_ignoreCard.gameObject);
                }
            }

            clickedCharacter = null;
        }
        
        private List<Character> GetIgnoreCharacters()
        {
            List<Character> _ignoreCharacters = new();
            foreach (var _character in GameManager.instance.GetCharacters(false))
            {
                if (_character.isChained || _character.isBlessed)
                {
                    _ignoreCharacters.Add(_character);
                }
            }
            return _ignoreCharacters;
        }
        
        public List<Role> GetIgnoreRoles()
        {
            List<Role> _ignoreRoles = new();
            foreach (var _character in GameManager.instance.GetCharacters(false))
            {
                if (_character.role.factionType != FactionType.chosen)
                {
                    _ignoreRoles.Add(_character.role);
                }
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
            BoardManager.instance.onCardClicked -= OnCardClicked;
            GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
            FocusManager.instance.UnfocusAll();
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            if (isSubscribedToMornings)
            {
                return;
            }
            var _awakeningStates = GameManager.instance.GetGameStates(typeof(AwakeningState));
            foreach (var _awakeningState in _awakeningStates)
            {
                _awakeningState.onStateEndServer += OnMorningBlessingServer;
            }

            isSubscribedToMornings = true;
        }
    }
}