#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Characters.Powers.Target;
using ChatSystem;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using Extensions;
using FMODUnity;
using GameLogic;
using GameLogic.GameStates;
using Network;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PDroolyHealing : Power
    {
        // Powers-POCO: the heal + reveal branch lives in DroolyHealingDecision (pure, EditMode-testable),
        // like every other active power. Only the engine-coupled bookkeeping stays here: the per-night
        // healed roster (read back at end of night for the public announce) and the outcome FMOD one-shot.
        private readonly DroolyHealingDecision _decision = new();

        public EventReference onHealSuccessfulSound;
        public EventReference onHealFailedSound;

        public List<ulong> healedCharactersThisNight = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }

        private void OnCharacterAndRolePicked(Character _character, Role _role)
        {
            if (!_character ||
                !CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Character) ||
                !CheckIsTargetValid(_role.ownerClientId, TargetUtils.TargetType.Role))
            {
                return;
            }
            TryHealServerRpc(_character.ownerClientId.Value, _role);
            OnUsed();
        }
        [Rpc(SendTo.Server)]
        private void TryHealServerRpc(ulong _healingCharacterId, Role _compareRole, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _healingCharacterId)) return; // NET-09: server-side use authorization
            // Snapshot the target's corruption state BEFORE the decision runs: its heal effect clears
            // isCorrupted / sets isHealed, so reading them afterwards would always report "not healable".
            var _choosedCharacter = characterManager.GetCharacter(_healingCharacterId, false);
            bool _wasHealable = _choosedCharacter &&
                                (_choosedCharacter.isCorrupted.Value || _choosedCharacter.isHealed.Value);

            PowerVerdict _verdict = RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value,
                targetSlot: (int)_healingCharacterId,
                secondaryTargetSlot: (int)_compareRole.ownerClientId,
                roster: Roster));

            bool _roleGuessed = _verdict == PowerVerdict.Correct;
            if (_roleGuessed)
            {
                healedCharactersThisNight.Add(_healingCharacterId);
            }

            // v1 PARITY, DELIBERATE: the one-shot still keys on the EFFECTIVE heal, not on the role guess,
            // so a right guess on an uncorrupted target keeps playing the failure sound while the new
            // onPowerVerdict channel reports Correct. Poyo owns that call — flagged, not silently changed.
            bool _healSuccess = _roleGuessed && _wasHealable;
            GameAudioManager.instance.PlayOneShotRpc(
                _healSuccess ? onHealSuccessfulSound.GetPath() : onHealFailedSound.GetPath(),
                characterManager.GetSafeRpcTarget(ownerClientId.Value));
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            var _gameManager = GameManager.For(NetworkManager);
            foreach (var _awakeningState in _gameManager.GetGameStates(typeof(AwakeningState)))
            {
                _awakeningState.onStateEndServer += OnNightEndedServer;
            }
        }

        private void OnNightEndedServer()
        {
            foreach (ulong _healedCharacterId in healedCharactersThisNight)
            {
                Character _healedCharacter = characterManager.GetCharacter(_healedCharacterId, false);
                if (!_healedCharacter)
                {
                    continue;
                }
                var _chatMessage = new ChatMessage
                {
                    message = $"{_healedCharacter.GetOwnerPseudo()} à été soigné(e) pendant la nuit avec {powerName.ToString()}",
                    senderClientId = GameValues.CHAT_SERVER_CLIENT_ID,
                    chatId = (int)ChatWindowIDs.Server
                };
                chatManager.ReceiveChatMessageRpc(_chatMessage, RpcTarget.Everyone);
            }

            healedCharactersThisNight.Clear();
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

        [SerializeField] private string[] pickerStepDescriptions;

        public override void StartUse()
        {
            base.StartUse();
            selectionFlowService.StartCharacterThenRoleSelection(targetValidator, OnCharacterAndRolePicked,
                new SelectionFlowOptions { stepDescriptions = pickerStepDescriptions });
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
            selectionFlowService.CancelSelection();
        }
    }
}
