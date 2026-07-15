using System;
using UnityEngine;
using Board;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;

namespace Characters.Powers
{
    [Serializable]
    public class PCursedVision : Power
    {
        // Powers-POCO v2 in-place wiring (Phase 3): logic lives in CursedVisionDecision (pure, EditMode-
        // tested). The card-effect id is a prefab-time constant fed to the decision (the Domain cannot see
        // the Game-side CardEffectID enum).
        // The decision's effects are OWNER-LOCAL presentation (corruption reveal via SetRevealLevel, the
        // card marker via AddCardEffect, the "élu" verdict via AddMessageLocal) plus self-RPC authoritative
        // mutations (CorruptPlayerServerRpc, NewTargetingRpc). They MUST run on the owner's client — exactly
        // as v1 did in the selection callback. An earlier v2 pass forwarded the pick through a SendTo.Server
        // RPC, which made the local writes land on the host instead of the caster (invisible to any non-host
        // player). Kept client-side; see spec-powers-poco-v2-architecture.md.
        private readonly CursedVisionDecision _decision = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
            _decision.CardEffectId = (int)CardEffectID.CursedVision;
        }

        private void OnCharacterPicked(Character _character)
        {
            if (!CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }

            // Runs on the owner's client (selection callback). The decision's owner-local effects apply here
            // directly; its authoritative effects self-RPC to the server. See the class-level note.
            RunClientDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value, targetSlot: (int)_character.ownerClientId.Value, roster: Roster));
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
        }
    }
}
