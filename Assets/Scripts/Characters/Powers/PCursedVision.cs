using System;
using UnityEngine;
using Board;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;

namespace Characters.Powers
{
    [Serializable]
    public class PCursedVision : Power
    {
        // Powers-POCO v2 in-place wiring (Phase 3): server logic lives in CursedVisionDecision (pure,
        // EditMode-tested). The selection flow stays client-side; the picked target is forwarded to a
        // server RPC whose body runs the decision + dispatch. The card-effect id is a prefab-time constant
        // fed to the decision (the Domain cannot see the Game-side CardEffectID enum).
        // PLAYTEST-REQUIRED before merge: the old path dispatched effects in the client selection callback;
        // moving them behind a ServerRpc shifts the server/client boundary (a behaviour change validated
        // only by a real 2-build playtest — see spec-powers-poco-v2-architecture.md, held-5).
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

            OnCardClickedRpc(_character.ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void OnCardClickedRpc(ulong _targetClientId)
        {
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value, targetSlot: (int)_targetClientId, roster: Roster));
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
