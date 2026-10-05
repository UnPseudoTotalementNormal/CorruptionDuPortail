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
        // LackOfAffectionDecision (pure, EditMode-tested). Its effects are local to the CONTACTED TARGET.
        // NET-09: the decision runs on the SERVER (it used to run on the target's client, from that client's
        // replica) with the target as localViewer, so the reveal and the chat line are delivered to the target.
        // The faction-keyed contact SOUND stays cosmetic and local to the target (OnPlayerContactedRpc).
        // NewTargeting stays exactly where v1 had it (the picker's callback).
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
            ContactServerRpc(_character.ownerClientId.Value);
        }

        [Rpc(SendTo.Server)]
        private void ContactServerRpc(ulong _targetClientId, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _targetClientId)) return; // NET-09

            // A real player (incl. the host) gets the contact line; a simulated bot has nobody to read it — the same
            // split the old target-client check made (local id == target).
            bool _isRealTarget = _targetClientId < 100;
            RunDecisionEffects(_decision, new PowerContext(
                    ownerSlot: (int)ownerClientId.Value,
                    targetSlot: (int)_targetClientId,
                    isTrueLocalTarget: _isRealTarget,
                    roster: Roster),
                localViewer: _targetClientId);

            OnPlayerContactedRpc(_targetClientId, characterManager.GetSafeRpcTarget(_targetClientId));
        }

        // Cosmetic only: the contacted target hears the faction-keyed contact sound.
        [Rpc(SendTo.SpecifiedInParams)]
        private void OnPlayerContactedRpc(ulong targetClientId, RpcParams rpcParams = default)
        {
            if (!characterManager.IsLocalOrSimulated(targetClientId)) return;

            bool _isTrueLocalTarget = characterManager.GetLocalClientId() == targetClientId;

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
