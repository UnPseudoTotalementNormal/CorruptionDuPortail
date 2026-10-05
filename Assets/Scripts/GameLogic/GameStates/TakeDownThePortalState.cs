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

        /// <summary>
        /// [LEAVE] Phase 2 (epic-player-leave-stability) — server hook the leave pipeline calls after a mid-game
        /// leaver is chained. This is a single-target flow: only the Mage (<see cref="mageCharacterOwnerId"/>) is
        /// subscribed to the click RPCs, so if the Mage leaves the step hangs forever. When the awaited Mage is the
        /// leaver we advance the loop so the portal step completes cleanly instead of hanging. Any other leaver is a
        /// spectator here and needs no unblock. Invoked only while this state is current
        /// (GameManager.UnblockCurrentStateAfterLeave).
        ///
        /// Double-fire note: the instant chain that precedes this call runs ChainingManager.ChainCharacterRpc,
        /// which — because the Mage carries the take-down-the-portal power — already re-set shouldActivate and
        /// re-pointed SetMageCharacterRpc to this same (now-chained) Mage. We deliberately do NOT touch
        /// shouldActivate or the mage id again here; we only advance. OnEndStateClient (fired by the transition)
        /// unsubscribes the client click handlers.
        /// </summary>
        public void OnPlayerLeftServer(ulong _ownerId)
        {
            if (!gameManager.NetworkManager.IsServer)
            {
                Debug.LogError("OnPlayerLeftServer can only be called on the server");
                return;
            }

            if (_ownerId != mageCharacterOwnerId)
            {
                return;
            }

            Debug.Log($"[LEAVE] TakeDownThePortal: awaited Mage {_ownerId} left — advancing the loop so the portal step cannot hang.");
            Loop.NextGameState();
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

            clickedCharacter = CharacterQuery.GetCharacter(_ownerId);
            // [LEAVE] Phase 2 — the clicked seat may have been removed (leaver) between click and server handling.
            // Null-guard the single-target flow rather than NRE: nothing to select, so leave the wait open.
            if (clickedCharacter == null)
            {
                Debug.Log($"[LEAVE] TakeDownThePortal: clicked character {_ownerId} no longer present — ignoring the click.");
                return;
            }

            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(UnsubscribeToCharacterClick),
                new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new []{mageCharacterOwnerId}));

            WaitForRoleClickServer();
        }

        private void OnRoleClickServer(ulong _ownerId)
        {
            // [LEAVE] Phase 2 — both the role-owner just clicked and the earlier clicked character can be
            // absent now (removed leaver). A missing character means the guess cannot be validated, so end the
            // step cleanly (same outcome as a wrong guess) instead of NRE-ing the single-target path.
            Character _clickedRoleCharacter = CharacterQuery.GetCharacter(_ownerId);
            if (_clickedRoleCharacter == null || clickedCharacter == null)
            {
                Debug.Log($"[LEAVE] TakeDownThePortal: role/character for the guess is missing (roleOwner={_ownerId}) — advancing the loop.");
                Loop.NextGameState();
                return;
            }

            var _clickedRole = _clickedRoleCharacter.role;

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

        private void SubscribeToCharacterClick() => boardManager.onCardClicked += OnCharacterClickClient;
        
        private void SubscribeToRoleClick()
        {
            if (!CardPickerManager.instance)
            {
                return;
            }
            
            Validator<(ulong targetId, TargetType targetType)> _validator = new();
            _validator.AddRule(_ctx => _ctx.targetType == TargetType.Role);
            selectionFlowService.StartRoleSelection(_validator, OnRoleClickClient);
        }
        
        private void UnsubscribeToCharacterClick() => boardManager.onCardClicked -= OnCharacterClickClient;
        
        private void UnsubscribeToRoleClick() => selectionFlowService.CancelSelection();
        

        private void WaitForCharacterClickServer()
        {
            Assert.IsTrue(gameManager.NetworkManager.IsServer);
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(UnHighlightAll), 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            
            var _ignoreCharactersList = GetIgnoreCharacters();
            ignoreCharacters = _ignoreCharactersList.ToList();

            if (ignoreCharacters.Count == CharacterQuery.GetCharacters(false).Count)
            {
                var _gameEndingState = (GameEndingState)gameManager.GetGameStates(typeof(GameEndingState)).First();
                var _newWinners = new Dictionary<WinningTeam, HashSet<ulong>>()
                {
                    { WinningTeam.anomaly , new HashSet<ulong>(CharacterQuery.GetCharacters(false)
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
            
            foreach (Card _card in boardManager.visibleCards)
            {
                Debug.Log("card visible from: " + _card.characterInfo.ownerClientId.Value);
                if (ignoreCharacters.Contains(_card.characterInfo.ownerClientId.Value))
                {
                    continue;
                }
                focusManager.FocusObject(_card.gameObject);
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
            focusManager.SetFocusOnType(FocusType.Roles);

            focusManager.FocusObject(boardManager.visibleCards
                .First(_c => _c.characterInfo.ownerClientId.Value == _clickedCharacterOwnerId).gameObject);
        }
        
        private void SetIgnoreCharactersRpc(ulong[] _ignoreCharacters)
        {
            ignoreCharacters = _ignoreCharacters.ToList();
        }
        
        private void UnHighlightAll()
        {
            focusManager.UnfocusAll();
        }
        
        private List<ulong> GetIgnoreCharacters()
        {
            List<ulong> _ignoreCharactersList = new();
            foreach (var _character in CharacterQuery.GetCharacters())
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

            // Rejoin step 1: a Mage chained while away (seat reserved, or chained when his grace expired) cannot
            // click: skip the step instead of waiting forever on him.
            if (gameManager.HasClientLeft(mageCharacterOwnerId))
            {
                Debug.Log($"[LEAVE] TakeDownThePortal: the Mage {mageCharacterOwnerId} is away — step skipped.");
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
            await UniTask.WaitUntil(() => boardManager.visibleCards.Count > 0);
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
            _ = boardManager.ShowAllPlayerCards();
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
