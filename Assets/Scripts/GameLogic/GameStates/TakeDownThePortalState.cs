#region

using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Board;
using Characters;
using Characters.WinningConditions;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using Extensions;
using FMODUnity;
using FocusSystem;
using GameLogic.Snapshot;
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
        // Server: true only while the step plays this loop (decided at its start by PortalTakedownRule, cleared at
        // its end). Read by the rejoin catch-up, the host's title and the autoplay driver.
        public bool shouldActivate;
        public ulong mageCharacterOwnerId;

        // Server: a portal carrier (the Mage Occulte) has been chained. On its own it no longer opens the step: every
        // anomaly must be chained too (GD, playtest 2026-11-20: the Mage chained first took the portal down at once,
        // then again each loop, e.g. when Abyss got chained).
        [System.NonSerialized] private bool carrierChained;
        // Server: the step already played this game: once per game.
        [System.NonSerialized] private bool portalPlayed;
        
        private List<ulong> ignoreCharacters;
        private Character clickedCharacter;
        
        public EventReference takeDownThePortalMusic;
        
        public void SetMageCharacterRpc(ulong _mageCharacterOwnerId)
        {
            mageCharacterOwnerId = _mageCharacterOwnerId;
        }

        /// <summary>Server: ChainingManager chained a character carrying the take-down-the-portal power. Records the
        /// carrier only; whether the step plays is decided when it starts (<see cref="DecideServer"/>).</summary>
        public void OnCarrierChainedServer(ulong _mageOwnerId)
        {
            carrierChained = true;
            mageCharacterOwnerId = _mageOwnerId;
        }

        /// <summary>Server: should the portal step play now (carrier chained, every anomaly chained, not played yet)?
        /// Also asked by the pre-vote victory check, so a last anomaly chained at night does not end the game before
        /// the Mage had his chance.</summary>
        public PortalTakedownRule.Decision DecideServer() =>
            PortalTakedownRule.Decide(GameSnapshotBuilder.FromLiveState(gameManager), carrierChained, portalPlayed);

        /// <summary>
        /// [LEAVE] Phase 2 (epic-player-leave-stability) — server hook the leave pipeline calls after a mid-game
        /// leaver is chained. This is a single-target flow: only the Mage (<see cref="mageCharacterOwnerId"/>) is
        /// subscribed to the click RPCs, so if the Mage leaves the step hangs forever. When the awaited Mage is the
        /// leaver we advance the loop so the portal step completes cleanly instead of hanging. Any other leaver is a
        /// spectator here and needs no unblock. Invoked only while this state is current
        /// (GameManager.UnblockCurrentStateAfterLeave).
        ///
        /// Double-fire note: the instant chain that precedes this call runs ChainingManager.ChainCharacterRpc,
        /// which — because the Mage carries the take-down-the-portal power — already re-recorded the carrier and
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

            Diagnostics.Logs.GameLog.Info("portal", "portal.skipped", "mage", mageCharacterOwnerId, "reason", "mage-left");
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
                Diagnostics.Logs.GameLog.Info("portal", "portal.pick.refused", "target", _ownerId, "sender", gameManager.CurrentStateRpcSenderId,
                    "mage", mageCharacterOwnerId, "reason", "ignored-target");
                return;
            }

            clickedCharacter = CharacterQuery.GetCharacter(_ownerId);
            // [LEAVE] Phase 2 — the clicked seat may have been removed (leaver) between click and server handling.
            // Null-guard the single-target flow rather than NRE: nothing to select, so leave the wait open.
            if (clickedCharacter == null)
            {
                Debug.Log($"[LEAVE] TakeDownThePortal: clicked character {_ownerId} no longer present — ignoring the click.");
                Diagnostics.Logs.GameLog.Info("portal", "portal.pick.refused", "target", _ownerId, "sender", gameManager.CurrentStateRpcSenderId,
                    "mage", mageCharacterOwnerId, "reason", "target-gone");
                return;
            }
            Diagnostics.Logs.GameLog.Info("portal", "portal.pick", "target", _ownerId, "sender", gameManager.CurrentStateRpcSenderId,
                "mage", mageCharacterOwnerId);

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
                Diagnostics.Logs.GameLog.Info("portal", "portal.guess", "roleOwner", _ownerId, "target", clickedCharacter != null ? clickedCharacter.ownerClientId.Value : ulong.MaxValue,
                    "correct", false, "reason", "character-gone");
                Loop.NextGameState();
                return;
            }

            var _clickedRole = _clickedRoleCharacter.role;

            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(UnsubscribeToRoleClick),
                new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new []{mageCharacterOwnerId}));

            Diagnostics.Logs.GameLog.Guarded(() => Diagnostics.Logs.GameLog.Info("portal", "portal.guess",
                "target", clickedCharacter.ownerClientId.Value, "guessed", _clickedRole != null ? _clickedRole.roleName.ToString() : null,
                "real", clickedCharacter.role != null ? clickedCharacter.role.roleName.ToString() : null,
                "correct", _clickedRole != null && _clickedRole.IsTheSameRole(clickedCharacter.role), "mage", mageCharacterOwnerId));

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
        
        private void SubscribeToRoleClick(ulong _targetOwnerId)
        {
            if (!CardPickerManager.instance)
            {
                return;
            }

            // A role whose every holder is already found (the Mage himself, a chained player, a role already guessed)
            // cannot be the target's: it is not offered (maquettes eventail-roles v1, C2: this power only). A role
            // also held by someone not found yet stays, a fake character's included (never revealed, so its role
            // stays offered and nothing tells it apart).
            Validator<(ulong targetId, TargetType targetType)> _validator = new();
            _validator.AddRule(_ctx => _ctx.targetType == TargetType.Role);
            _validator.AddRule(_ctx => !IsRoleFound(_ctx.targetId));
            Diagnostics.Logs.GameLog.Guarded(() => Diagnostics.Logs.GameLog.Info("portal", "portal.roles.hidden",
                "target", _targetOwnerId, "mage", mageCharacterOwnerId,
                "found", CharacterQuery.GetCharacters().Where(_c => IsRoleFound(_c.ownerClientId.Value))
                    .Select(_c => _c.ownerClientId.Value).ToList()));

            Character _target = CharacterQuery.GetCharacter(_targetOwnerId);
            string _pseudo = _target != null ? _target.GetOwnerPseudo() : string.Empty;
            SelectionFlowOptions _options = new()
            {
                pinnedCharacter = _target,
                // French elision: « d'Isa1708 », « de Wouh ».
                stepDescriptions = new[] { StartsWithVowel(_pseudo) ? "Devinez le rôle d'{0}." : "Devinez le rôle de {0}." }, // loc
                stepDescriptionArgs = new object[] { _pseudo },
            };
            selectionFlowService.StartRoleSelection(_validator, OnRoleClickClient, _options);
        }

        private bool IsRoleFound(ulong _ownerId) =>
            _ownerId == mageCharacterOwnerId
            || gameInfoRevealer.GetCharacterInfo(_ownerId).isRoleRevealed >= RevealLevel.Public;

        private static bool StartsWithVowel(string _text) =>
            !string.IsNullOrEmpty(_text) && "aeiouyhàâäéèêëîïôöùûüAEIOUYHÀÂÄÉÈÊËÎÏÔÖÙÛÜ".IndexOf(_text[0]) >= 0; // loc-ok: letters tested, never shown
        
        private void UnsubscribeToCharacterClick() => boardManager.onCardClicked -= OnCharacterClickClient;
        
        private void UnsubscribeToRoleClick() => selectionFlowService.CancelSelection();
        

        private void WaitForCharacterClickServer()
        {
            Assert.IsTrue(gameManager.NetworkManager.IsServer);
            
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(UnHighlightAll), 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            
            var _ignoreCharactersList = GetIgnoreCharacters();
            ignoreCharacters = _ignoreCharactersList.ToList();
            Diagnostics.Logs.GameLog.Info("portal", "portal.wait.pick", "mage", mageCharacterOwnerId, "ignored", ignoreCharacters);

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
                Diagnostics.Logs.GameLog.Info("portal", "portal.anomalywin", "mage", mageCharacterOwnerId,
                    "winners", _newWinners[WinningTeam.anomaly].ToList(), "reason", "no-target-left");
                
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
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(SubscribeToRoleClick),
                new NetworkSerializableObject[] { new(clickedCharacter.ownerClientId.Value) },
                new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new []{mageCharacterOwnerId}));
        }
        
        private void HighlightRolesRpc(ulong _clickedCharacterOwnerId)
        {
            focusManager.SetFocusOnType(FocusType.Roles);

            // A peer that rejoined during this step has no board cards yet (they are dealt at the next state entry).
            var _card = boardManager.visibleCards
                .FirstOrDefault(_c => _c.characterInfo.ownerClientId.Value == _clickedCharacterOwnerId);
            if (_card != null)
            {
                focusManager.FocusObject(_card.gameObject);
            }
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
            carrierChained = false;
            portalPlayed = false;
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            // The step plays once, when EVERY anomaly is chained, not as soon as the Mage is (that latched the step on
            // for every later loop: the portal came down when the Mage got chained, then again when Abyss did).
            PortalTakedownRule.Decision _decision = DecideServer();
            shouldActivate = _decision.Activate;
            Diagnostics.Logs.GameLog.Info("portal", "portal.decision", "activate", _decision.Activate, "reason", _decision.Reason,
                "mage", mageCharacterOwnerId, "carrierChained", carrierChained, "played", portalPlayed,
                "freeAnomalies", _decision.FreeAnomalies);
            if (!shouldActivate)
            {
                Debug.Log($"TakeDownThePortalState is not activated ({_decision.Reason})");
                Diagnostics.Logs.GameLog.Info("portal", "portal.skipped", "reason", _decision.Reason, "mage", mageCharacterOwnerId,
                    "freeAnomalies", _decision.FreeAnomalies);
                _ = Loop.WaitAFrameAndNextGameState();
                return;
            }
            portalPlayed = true;
            // Re-sent now: a peer that joined back since the Mage got chained missed the first broadcast.
            gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(SetMageCharacterRpc),
                new NetworkSerializableObject[] { new(mageCharacterOwnerId) }, new CustomRpcParams(CustomRpcParams.RpcTargetType.all));

            // Rejoin step 1: a Mage chained while away (seat reserved, or chained when his grace expired) cannot
            // click: skip the step instead of waiting forever on him.
            if (gameManager.HasClientLeft(mageCharacterOwnerId))
            {
                Debug.Log($"[LEAVE] TakeDownThePortal: the Mage {mageCharacterOwnerId} is away — step skipped.");
                Diagnostics.Logs.GameLog.Info("portal", "portal.skipped", "mage", mageCharacterOwnerId, "reason", "mage-away");
                _ = Loop.WaitAFrameAndNextGameState();
                return;
            }

            Diagnostics.Logs.GameLog.Info("portal", "portal.active", "mage", mageCharacterOwnerId);
            GameAudioManager.instance.PlayMusicRpc(takeDownThePortalMusic.GetPath(), 
                gameManager.NetworkManager.RpcTarget.ClientsAndHost);

            _ = WaitForCardsToBeVisible();
        }

        private async UniTaskVoid WaitForCardsToBeVisible()
        {
            int _from = gameManager.currentGameStateIndex.Value;
            await UniTask.WaitForSeconds(3);
            await UniTask.WaitUntil(() => boardManager == null || boardManager.visibleCards.Count > 0);
            // The step may have been left during the wait (the Mage left, a leave victory): opening the portal
            // selection now would highlight the cards in the next phase and wait on a gone Mage (N3, same as N2).
            if (boardManager == null || gameManager.currentGameStateIndex.Value != _from)
            {
                Debug.Log($"[LEAVE] TakeDownThePortal: step {_from} was left during the card wait — selection not opened.");
                Diagnostics.Logs.GameLog.Info("portal", "portal.selection.skipped", "mage", mageCharacterOwnerId, "step", _from,
                    "reason", boardManager == null ? "no-board" : "step-left");
                return;
            }
            WaitForCharacterClickServer();
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
            shouldActivate = false;
            
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
