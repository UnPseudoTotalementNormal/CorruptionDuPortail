#region

using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Board;
using Characters;
using Characters.WinningConditions;
using Cysharp.Threading.Tasks;
using Extensions;
using FMODUnity;
using FocusSystem;
using GameLogic.Validation;
using Network;
using UI.BoardUI;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using static Characters.Powers.Target.TargetUtils;
using FocusType = FocusSystem.FocusType;

#endregion

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "TakeDownThePortalState", menuName = "GameStates/TakeDownThePortalState")]
    public class TakeDownThePortalState : GameState
    {
        public bool shouldActivate; //TODO : Temporary
        public ulong mageCharacterOwnerId;
        
        private List<ulong> ignoreCharacters;
        private Character clickedCharacter;
        
        public EventReference takeDownThePortalMusic;
        
        public void SetMageCharacterRpc(ulong _mageCharacterOwnerId)
        {
            mageCharacterOwnerId = _mageCharacterOwnerId;
        }

        
        
        private void OnCharacterClickClient(Card _card)
        {
            ulong _characterOwnerId = _card.characterInfo.ownerClientId.Value;
            if (ignoreCharacters.Contains(_characterOwnerId))
            {
                return;
            }
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(OnCharacterClickServer), 
                new NetworkSerializableObject[] { new(_characterOwnerId)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
        }
        
        private void OnRoleClickClient(Role _role)
        {
            if (_role == null)
            {
                return;
            }
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(OnRoleClickServer), 
                new NetworkSerializableObject[] { new(_role.ownerClientId)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
        }
        
        private void OnCharacterClickServer(ulong _ownerId)
        {
            if (ignoreCharacters.Contains(_ownerId))
            {
                return;
            }
            
            clickedCharacter = characterManager.GetCharacter(_ownerId);
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(UnsubscribeToCharacterClick), 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new []{mageCharacterOwnerId}));
            
            WaitForRoleClickServer();
        }
        
        private void OnRoleClickServer(ulong _ownerId)
        {
            var _clickedRole = characterManager.GetCharacter(_ownerId).role;

            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(UnsubscribeToRoleClick), 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new []{mageCharacterOwnerId}));
            
            if (!_clickedRole.IsTheSameRole(clickedCharacter.role))
            {
                Loop.NextGameState();
                return;
            }
            
            gameInfoRevealer.SetRevealLevelRpc(clickedCharacter.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Public, true,
                gameManager.RpcTarget.Everyone);
            
            WaitForCharacterClickServer();
        }

        private void SubscribeToCharacterClick() => BoardManager.instance.onCardClicked += OnCharacterClickClient;
        
        private void SubscribeToRoleClick()
        {
            if (!CardPickerManager.instance)
            {
                return;
            }
            
            Validator<(ulong targetId, TargetType targetType)> _validator = new();
            _validator.AddRule(_ctx => _ctx.targetType == TargetType.Role);
            SelectionFlowService.instance.StartRoleSelection(_validator, OnRoleClickClient);
        }
        
        private void UnsubscribeToCharacterClick() => BoardManager.instance.onCardClicked -= OnCharacterClickClient;
        
        private void UnsubscribeToRoleClick() => SelectionFlowService.instance.CancelSelection();
        

        private void WaitForCharacterClickServer()
        {
            Assert.IsTrue(gameManager.NetworkManager.IsServer);
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(UnHighlightAll), 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            
            var _ignoreCharactersList = GetIgnoreCharacters();
            ignoreCharacters = _ignoreCharactersList.ToList();

            if (ignoreCharacters.Count == characterManager.GetCharacters(false).Count)
            {
                var _gameEndingState = (GameEndingState)gameManager.GetGameStates(typeof(GameEndingState)).First();
                var _newWinners = new Dictionary<WinningTeam, HashSet<ulong>>()
                {
                    { WinningTeam.anomaly , new HashSet<ulong>(characterManager.GetCharacters(false)
                        .Where(_c => _c.role.factionType == FactionType.anomaly)
                        .Select(_c => _c.ownerClientId.Value)) },
                };
                _gameEndingState.SetWinnersServer(_newWinners);
                
                Loop.SetGameState(_gameEndingState);
                return;
            }
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(SetIgnoreCharactersRpc),
                new NetworkSerializableObject[] { new(ignoreCharacters.ToArray()) }, new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(HighlightCharactersRpc), new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(SubscribeToCharacterClick), new CustomRpcParams(CustomRpcParams.RpcTargetType.single, 
                new []{mageCharacterOwnerId}));
        }


        private void HighlightCharactersRpc()
        {
            
            foreach (Card _card in BoardManager.instance.visibleCards)
            {
                Debug.Log("card visible from: " + _card.characterInfo.ownerClientId.Value);
                if (ignoreCharacters.Contains(_card.characterInfo.ownerClientId.Value))
                {
                    continue;
                }
                FocusManager.instance.FocusObject(_card.gameObject);
            }
        }
        
        private void WaitForRoleClickServer()
        {
            Assert.IsTrue(gameManager.NetworkManager.IsServer);
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(HighlightRolesRpc), 
                new NetworkSerializableObject[] {new(clickedCharacter.ownerClientId.Value)} ,new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(SubscribeToRoleClick), new CustomRpcParams(CustomRpcParams.RpcTargetType.single, 
                new []{mageCharacterOwnerId}));
        }
        
        private void HighlightRolesRpc(ulong _clickedCharacterOwnerId)
        {
            FocusManager.instance.SetFocusOnType(FocusType.Roles);

            FocusManager.instance.FocusObject(BoardManager.instance.visibleCards
                .First(_c => _c.characterInfo.ownerClientId.Value == _clickedCharacterOwnerId).gameObject);
        }
        
        private void SetIgnoreCharactersRpc(ulong[] _ignoreCharacters)
        {
            ignoreCharacters = _ignoreCharacters.ToList();
        }
        
        private void UnHighlightAll()
        {
            FocusManager.instance.UnfocusAll();
        }
        
        private List<ulong> GetIgnoreCharacters()
        {
            List<ulong> _ignoreCharactersList = new();
            foreach (var _character in characterManager.GetCharacters())
            {
                if (_character.isFake)
                {
                    _ignoreCharactersList.Add(_character.ownerClientId.Value);
                    continue;
                }
                
                if (_character.ownerClientId.Value == mageCharacterOwnerId)
                {
                    _ignoreCharactersList.Add(_character.ownerClientId.Value);
                    continue;
                }
                
                if (gameInfoRevealer.GetCharacterInfo(_character.ownerClientId.Value).isRoleRevealed >= RevealLevel.Public)
                {
                    _ignoreCharactersList.Add(_character.ownerClientId.Value);
                    continue;
                }
            }

            return _ignoreCharactersList;
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
                Debug.Log("TakeDownThePortalState is not activated");
                _ = Loop.WaitAFrameAndNextGameState();
                return;
            }

            GameAudioManager.instance.PlayMusicRpc(takeDownThePortalMusic.GetPath(), 
                gameManager.NetworkManager.RpcTarget.ClientsAndHost);

            _ = WaitForCardsToBeVisible();
        }

        private async UniTaskVoid WaitForCardsToBeVisible()
        {
            await UniTask.WaitForSeconds(3);
            await UniTask.WaitUntil(() => BoardManager.instance.visibleCards.Count > 0);
            WaitForCharacterClickServer();
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
            
            GameAudioManager.instance.StopMusicRpc(takeDownThePortalMusic.GetPath(), 
                gameManager.NetworkManager.RpcTarget.ClientsAndHost);
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
            _ = BoardManager.instance.ShowAllPlayerCards();
        }
        
        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
            UnsubscribeToCharacterClick();
            UnsubscribeToRoleClick();
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
