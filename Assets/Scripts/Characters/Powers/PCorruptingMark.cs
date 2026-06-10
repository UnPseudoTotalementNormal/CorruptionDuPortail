#region

using System;
using ArrowSystem;
using Characters.Powers.Interfaces;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PCorruptingMark : Power, ICorrupterPower, IConcentratedPowerEffect
    {
        [field:SerializeField] public string concentratedEffectDescription { get; set; }
        
        private NetworkVariable<ulong> lastCorruptedCharacterId = new(9999999);
        
        public event Action<Character> onCharacterCorruptionSuccessful;
        public event Action<Character> onCharacterCorruptionFailed;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }

        public void InvokeOnCharacterCorruptionSuccessful(ulong characterId)
        {
            var _character = GameManager.instance.characterManager.GetCharacter(characterId);
            onCharacterCorruptionSuccessful?.Invoke(_character);
        }
        public void InvokeOnCharacterCorruptionFailed(ulong characterId)
        {
            var _character = GameManager.instance.characterManager.GetCharacter(characterId);
            onCharacterCorruptionFailed?.Invoke(_character);
        }
        private void OnCharacterPicked(Character _character)
        {
            var _clickedCharacterId = _character.ownerClientId.Value;
            if (!CheckIsTargetValid(_clickedCharacterId, TargetUtils.TargetType.Character))
            {
                PowerEffectTrace.Record(new CorruptionFailed((int)_clickedCharacterId));
                InvokeOnCharacterCorruptionFailedRpc(_clickedCharacterId);
                return;
            }
            OnCardClickedRpc(_clickedCharacterId);
            PowerEffectTrace.Record(new RevealInfo((int)_clickedCharacterId, RevealField.CorruptRevealed,
                RevealVisibility.Personal, (int)ownerClientId.Value, false));
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _clickedCharacterId, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal, ownerClientId.Value);
            OnUsed();
        }

        private readonly PowerResolver _resolver = new();

        [Rpc(SendTo.Server)]
        private void OnCardClickedRpc(ulong _clickedCharacterId)
        {
            // Story 4.3: decision-only resolution in Domain; the adapter dispatches the bricks.
            // StoreLastCorrupted (private NV) + CorruptionSucceeded (power event RPC) are power-LOCAL.
            var _effects = _resolver.ResolveCorruptingMarkClick((int)ownerClientId.Value, (int)_clickedCharacterId);
            foreach (var _effect in _effects)
            {
                PowerEffectDispatcher.Dispatch(_effect, ApplyLocalEffect);
            }
        }

        private void ApplyLocalEffect(EffectDescriptor _effect)
        {
            switch (_effect)
            {
                case StoreLastCorrupted _store:
                    lastCorruptedCharacterId.Value = (ulong)_store.TargetSlot;
                    break;
                case CorruptionSucceeded _succeeded:
                    InvokeOnCharacterCorruptionSuccessfulRpc((ulong)_succeeded.Slot);
                    break;
                default:
                    throw new NotSupportedException($"PCorruptingMark: unexpected local brick {_effect}");
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
            SelectionFlowService.instance.StartCharacterSelection(targetValidator, OnCharacterPicked,
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
            SelectionFlowService.instance.CancelSelection();
            PowerEffectTrace.Record(DestroyAllArrows.Instance);
            ArrowManager.instance.DestroyAllArrows();
        }
        
        public void OnConcentratedEffectServer()
        {
            PowerEffectTrace.Record(new RevealInfo((int)lastCorruptedCharacterId.Value, RevealField.RoleRevealed,
                RevealVisibility.Personal, (int)ownerClientId.Value, true));
            GameManager.instance.gameInfoRevealer.SendRevealLevelRpc(
                lastCorruptedCharacterId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, ownerClientId.Value, true);
        }
    }
}
