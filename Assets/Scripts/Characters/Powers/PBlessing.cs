using System;
using System.Collections.Generic;
using System.Linq;
using FocusSystem;
using GameLogic;
using GameLogic.GameStates;
using Network;
using Unity.Netcode;
using UnityEngine.Assertions;

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
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Characters);
            FocusManager.instance.FocusObject(_clickedCard.gameObject);
        }
        
        private void OnCharacterBarClicked(Character _character)
        {
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

            clickedCharacter = null;
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