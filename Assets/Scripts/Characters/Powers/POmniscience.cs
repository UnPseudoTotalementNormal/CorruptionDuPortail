using System;
using UnityEngine;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;

namespace Characters.Powers
{
    [Serializable]
    public class POmniscience : Power //TODO: rework win condition to use power instead of creating a wincondition
    {
        public ulong hackedCharacterClientId = HACKED_CHARACTER_DEFAULT;
        public const ulong HACKED_CHARACTER_DEFAULT = 4994996541621;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }
        
        private void OnCharacterPicked(Character _character)
        {
            if (!CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _character.ownerClientId.Value);
            OnCardClickedServerRpc(_character.ownerClientId.Value);
            OnUsed();
        }
        private readonly PowerResolver _resolver = new();

        [Rpc(SendTo.Server)]
        private void OnCardClickedServerRpc(ulong _targetClientId)
        {
            OnCardClickedRpc(_targetClientId);
        }
        private void OnCardClickedRpc(ulong _targetClientId)
        {
            // Story 4.4 (the hack): decision-only resolution in Domain; the adapter dispatches.
            // StoreHackTarget (the public hackedCharacterClientId field) is power-LOCAL. The
            // RevealInfo(Broadcast:true) brick is the notify-to-target intention.
            var _effects = _resolver.ResolveOmniscienceClick((int)ownerClientId.Value, (int)_targetClientId);
            foreach (var _effect in _effects)
            {
                PowerEffectDispatcher.Dispatch(_effect, ApplyLocalEffect);
            }
        }

        private void ApplyLocalEffect(EffectDescriptor _effect)
        {
            switch (_effect)
            {
                case StoreHackTarget _store:
                    hackedCharacterClientId = (ulong)_store.TargetSlot;
                    break;
                default:
                    throw new NotSupportedException($"POmniscience: unexpected local brick {_effect}");
            }
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
        }
    }
}