using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.WinningConditions;
using Cysharp.Threading.Tasks;
using FocusSystem;
using Network;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using FocusType = FocusSystem.FocusType;

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "TakeDownThePortalState", menuName = "GameStates/TakeDownThePortalState")]
    public class TakeDownThePortalState : GameState
    {
        public bool shouldActivate; //TODO : Temporary
        public ulong mageCharacterOwnerId;
        
        private ulong[] ignoreCharacters;
        private Character clickedCharacter;
        
        public void SetMageCharacter(ulong _mageCharacterOwnerId)
        {
            mageCharacterOwnerId = _mageCharacterOwnerId;
        }

        private void OnCharacterClickClient(Card _card)
        {
            ulong _characterOwnerId = _card.characterInfo.ownerClientId;
            if (ignoreCharacters.Contains(_characterOwnerId))
            {
                return;
            }
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(OnCharacterClickServer), 
                new NetworkSerializableObject[] { new(_characterOwnerId)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
        }
        
        private void OnRoleClickClient(Character _character)
        {
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(OnRoleClickServer), 
                new NetworkSerializableObject[] { new(_character.ownerClientId)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
        }
        
        private void OnCharacterClickServer(ulong _ownerId)
        {
            if (ignoreCharacters.Contains(_ownerId))
            {
                return;
            }
            
            clickedCharacter = GameManager.instance.GetCharacter(_ownerId);
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(UnsubscribeToCharacterClick), 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new []{mageCharacterOwnerId}));
            
            WaitForRoleClickServer();
        }
        
        private void OnRoleClickServer(ulong _ownerId)
        {
            var _clickedRole = GameManager.instance.GetCharacter(_ownerId).role;

            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(UnsubscribeToRoleClick), 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new []{mageCharacterOwnerId}));
            
            if (!_clickedRole.IsTheSameRole(clickedCharacter.role))
            {
                gameManager.NextGameState();
                return;
            }
            
            GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(clickedCharacter.ownerClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Public, 
                gameManager.RpcTarget.Everyone);
        }

        private void SubscribeToCharacterClick() => BoardManager.instance.onCardClicked += OnCharacterClickClient;
        
        private void SubscribeToRoleClick() => gameManager.charactersBar.onCharacterBarClicked += OnRoleClickClient;
        
        private void UnsubscribeToCharacterClick() => BoardManager.instance.onCardClicked -= OnCharacterClickClient;
        
        private void UnsubscribeToRoleClick() => gameManager.charactersBar.onCharacterBarClicked -= OnRoleClickClient;
        

        private void WaitForCharacterClickServer()
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer);
            
            List<ulong> _ignoreCharactersList = new();
            foreach (var _character in gameManager.GetCharacters())
            {
                if (_character.ownerClientId != mageCharacterOwnerId)
                {
                    _ignoreCharactersList.Add(_character.ownerClientId);
                    continue;
                }
                
                if (gameManager.gameInfoRevealer.GetCharacterInfo(_character.ownerClientId).isRoleRevealed >= RevealLevel.Public)
                {
                    _ignoreCharactersList.Add(_character.ownerClientId);
                }
            }
            ignoreCharacters = _ignoreCharactersList.ToArray();

            if (ignoreCharacters.Length == gameManager.GetCharacters(false).Count)
            {
                var _gameEndingState = (GameEndingState)gameManager.GetGameStates(typeof(GameEndingState)).First();
                var _newWinners = new Dictionary<WinningTeam, HashSet<ulong>>()
                {
                    { WinningTeam.anomaly , new HashSet<ulong>(gameManager.GetCharacters(false)
                        .Where(_c => _c.role.factionType == FactionType.anomaly)
                        .Select(_c => _c.ownerClientId)) },
                };
                _gameEndingState.SetWinnersServer(_newWinners);
                
                gameManager.SetGameState(_gameEndingState);
                return;
            }
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(SetIgnoreCharactersRpc),
                new NetworkSerializableObject[] { new(ignoreCharacters) }, new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(HighlightCharactersRpc), new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(SubscribeToCharacterClick), new CustomRpcParams(CustomRpcParams.RpcTargetType.single, 
                new []{mageCharacterOwnerId}));
        }
        
        private void HighlightCharactersRpc()
        {
            FocusManager.instance.SetFocusOnType(FocusType.Cards);

            foreach (var _card in BoardManager.instance.visibleCards)
            {
                if (ignoreCharacters.Contains(_card.characterInfo.ownerClientId))
                {
                    FocusManager.instance.UnfocusObject(_card.gameObject);
                }
            }
        }
        
        private void WaitForRoleClickServer()
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer);
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(HighlightRolesRpc), new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(SubscribeToRoleClick), new CustomRpcParams(CustomRpcParams.RpcTargetType.single, 
                new []{mageCharacterOwnerId}));
        }
        
        private void HighlightRolesRpc()
        {
            FocusManager.instance.SetFocusOnType(FocusType.Roles);
        }
        
        private void SetIgnoreCharactersRpc(ulong[] _ignoreCharacters)
        {
            ignoreCharacters = _ignoreCharacters;
        }
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
            shouldActivate = false;
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            if (!shouldActivate)
            {
                gameManager.NextGameState();
                return;
            }
            
            WaitForCharacterClickServer();
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
        }
        
        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}