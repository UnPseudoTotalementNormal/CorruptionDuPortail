using System;
using UnityEngine;
using Board;
using Characters.Powers.Target;
using ChatSystem;
using CorruptionDuPortail.Domain;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;

namespace Characters.Powers
{
    [Serializable]
    public class PCursedVision : Power
    {
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }

        private readonly PowerResolver _resolver = new();

        private void OnCharacterPicked(Character _character)
        {
            if (!CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }

            // Story 4.1: decision-only resolution in Domain; the adapter dispatches the bricks.
            var _effects = _resolver.ResolveCursedVision(
                (int)ownerClientId.Value,
                (int)_character.ownerClientId.Value,
                _character.role.factionType == FactionType.chosen,
                _character.GetOwnerPseudo(),
                (int)CardEffectID.CursedVision,
                (int)ChatWindowIDs.Server);

            foreach (var _effect in _effects)
            {
                PowerEffectDispatcher.Dispatch(_effect);
            }

            OnUsed();
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
