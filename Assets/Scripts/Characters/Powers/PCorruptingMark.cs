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

        private NetworkVariable<ulong> lastCorruptedCharacterId = new(9999999);

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
            OnCardClickedRpc(_clickedCharacterId);
            gameInfoRevealer.SetRevealLevel(
                _clickedCharacterId, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal, ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void OnCardClickedRpc(ulong _clickedCharacterId, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _clickedCharacterId)) return; // NET-09: server-side use authorization
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value, targetSlot: (int)_clickedCharacterId), SelfState);
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
            gameInfoRevealer.SendRevealLevelRpc(
                lastCorruptedCharacterId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, ownerClientId.Value, true);
        }
    }
}
