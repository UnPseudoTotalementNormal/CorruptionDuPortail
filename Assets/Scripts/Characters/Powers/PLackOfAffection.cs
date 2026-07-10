#region

using System;
using UnityEngine;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using Extensions;
using FMODUnity;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PLackOfAffection : Power
    {
        public EventReference onContactedAsChosenSound;
        public EventReference onContactedAsMarginalSound;
        public EventReference onContactedAsAnomalySound;

        // Powers-POCO v2 in-place wiring (Phase 3): the reveal + local-chat logic lives in
        // LackOfAffectionDecision (pure, EditMode-tested). This power is special — its effect runs on the
        // CONTACTED TARGET's client, so the decision is dispatched there via RunClientDecisionEffects (no
        // server guard), keyed by PowerContext.IsTrueLocalTarget. The faction-keyed contact SOUND stays
        // adapter-side and local. NewTargeting stays exactly where v1 had it (the picker's callback).
        // PLAYTEST-REQUIRED before merge: needs a real second client to exercise the target-client path
        // (held-5 — see spec-powers-poco-v2-architecture.md).
        private readonly LackOfAffectionDecision _decision = new();

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

            roleTargetSystem.NewTargeting(ownerClientId.Value, _character.ownerClientId.Value);
            OnUsed();
            OnPlayerContactedRpc(_character.ownerClientId.Value, ownerClientId.Value, characterManager.GetSafeRpcTarget(_character.ownerClientId.Value));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void OnPlayerContactedRpc(ulong targetClientId, ulong senderClientId, RpcParams rpcParams = default)
        {
            if (!characterManager.IsLocalOrSimulated(targetClientId)) return;

            bool _isTrueLocalTarget = characterManager.GetLocalClientId() == targetClientId;

            // The decision resolves on THIS (the contacted target's) client: reveal the sender's role when
            // the target is a "chosen", and post the local contact line only for the true-local target.
            RunClientDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)senderClientId,
                targetSlot: (int)targetClientId,
                isTrueLocalTarget: _isTrueLocalTarget,
                roster: Roster));

            if (_isTrueLocalTarget)
            {
                Character _targetCharacter = characterManager.GetCharacter(targetClientId, false);
                switch (_targetCharacter.role.factionType)
                {
                    case FactionType.chosen:
                        onContactedAsChosenSound.TryPlayOneShot();
                        break;
                    case FactionType.marginal:
                        onContactedAsMarginalSound.TryPlayOneShot();
                        break;
                    case FactionType.anomaly:
                        onContactedAsAnomalySound.TryPlayOneShot();
                        break;
                }
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
