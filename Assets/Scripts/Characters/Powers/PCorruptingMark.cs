#region

using System;
using ArrowSystem;
using Characters.Powers.Interfaces;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PCorruptingMark : Power, ICorrupterPower, IConcentratedPowerEffect,
        ILastCorruptedState, ICorruptionEvents
    {
        [field:SerializeField] public string concentratedEffectDescription { get; set; }

        private const ulong NoCorruptedTarget = 9999999;
        private NetworkVariable<ulong> lastCorruptedCharacterId = new(NoCorruptedTarget);

        public event Action<Character> onCharacterCorruptionSuccessful;
        public event Action<Character> onCharacterCorruptionFailed;

        // Powers-POCO v2: server logic in CorruptingMarkDecision (pure). last-corrupted NV + the
        // corruption-outcome event are power-local, reached by the effect executors through SelfState via
        // ILastCorruptedState / ICorruptionEvents. Behaviour-identical to the old resolver path.
        private readonly CorruptingMarkDecision _decision = new();

        void ILastCorruptedState.StoreLastCorrupted(int _slot) => lastCorruptedCharacterId.Value = (ulong)_slot;
        void ICorruptionEvents.RaiseSucceeded(int _slot) => InvokeOnCharacterCorruptionSuccessfulRpc((ulong)_slot);
        void ICorruptionEvents.RaiseFailed(int _slot) => InvokeOnCharacterCorruptionFailedRpc((ulong)_slot);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }

        public void InvokeOnCharacterCorruptionSuccessful(ulong characterId)
        {
            var _character = characterManager.GetCharacter(characterId);
            onCharacterCorruptionSuccessful?.Invoke(_character);
        }
        public void InvokeOnCharacterCorruptionFailed(ulong characterId)
        {
            var _character = characterManager.GetCharacter(characterId);
            onCharacterCorruptionFailed?.Invoke(_character);
        }
        private void OnCharacterPicked(Character _character)
        {
            var _clickedCharacterId = _character.ownerClientId.Value;
            if (!CheckIsTargetValid(_clickedCharacterId, TargetUtils.TargetType.Character))
            {
                InvokeOnCharacterCorruptionFailedRpc(_clickedCharacterId);
                return;
            }
            // NET-10: the caster learning that its target is corrupted is written by the SERVER (OnCardClickedRpc)
            // into the knowledge ledger — it used to be a client-local write the server never knew about.
            OnCardClickedRpc(_clickedCharacterId);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void OnCardClickedRpc(ulong _clickedCharacterId, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _clickedCharacterId)) return; // NET-09: server-side use authorization
            // The Dryade's blessing makes a player immune to this power for the game. The picker already hides blessed
            // players, but that is a client-side filter: re-check on the server. The use stays consumed (the client
            // already sent OnUsed); the corruption simply fails, and the concentration reveal finds no target.
            Character _target = characterManager.GetCharacter(_clickedCharacterId, false);
            if (_target == null || _target.isBlessed.Value)
            {
                lastCorruptedCharacterId.Value = NoCorruptedTarget;
                InvokeOnCharacterCorruptionFailedRpc(_clickedCharacterId);
                return;
            }
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value, targetSlot: (int)_clickedCharacterId), SelfState);
            // NET-10: the caster knows its target is now corrupted (ledger write + push to the caster).
            if (gameInfoRevealer != null)
            {
                gameInfoRevealer.SendRevealLevelRpc(
                    _clickedCharacterId, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal, ownerClientId.Value);
            }
        }

        [Rpc(SendTo.Everyone)]
        private void InvokeOnCharacterCorruptionSuccessfulRpc(ulong characterId)
        {
            InvokeOnCharacterCorruptionSuccessful(characterId);
        }

        [Rpc(SendTo.Everyone)]
        private void InvokeOnCharacterCorruptionFailedRpc(ulong characterId)
        {
            InvokeOnCharacterCorruptionFailed(characterId);
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

        [SerializeField] private string pickerDescription;

        public override void StartUse()
        {
            base.StartUse();
            selectionFlowService.StartCharacterSelection(targetValidator, OnCharacterPicked,
                new SelectionFlowOptions { stepDescriptions = new[] { pickerDescription } });
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
            ArrowManager.instance.DestroyAllArrows();
        }
        
        public void OnConcentratedEffectServer()
        {
            if (lastCorruptedCharacterId.Value == NoCorruptedTarget) return; // refused target: nothing to reveal
            gameInfoRevealer.SendRevealLevelRpc(
                lastCorruptedCharacterId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, ownerClientId.Value, true);
        }
    }
}
