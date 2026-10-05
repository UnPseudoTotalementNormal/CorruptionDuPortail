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
        // Powers-POCO v2 in-place wiring (Phase 3): logic lives in CursedVisionDecision (pure, EditMode-
        // tested). The card-effect id is a prefab-time constant fed to the decision (the Domain cannot see
        // the Game-side CardEffectID enum).
        // NET-09: the decision runs on the SERVER from server state. Its OWNER-LOCAL presentation (corruption
        // reveal, card marker, the "élu" verdict chat line) is delivered to the caster through RunDecisionEffects'
        // localViewer — the routing problem that once made a server-side version land on the host is solved in the
        // executors instead of by deciding on a client replica.
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

            CursedVisionServerRpc(_character.ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void CursedVisionServerRpc(ulong _targetCharacterId, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _targetCharacterId)) return; // NET-09

            RunDecisionEffects(_decision, new PowerContext(
                    ownerSlot: (int)ownerClientId.Value, targetSlot: (int)_targetCharacterId, roster: Roster),
                localViewer: ownerClientId.Value);
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
