using UnityEngine;
using UnityEngine.Assertions;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;

namespace Characters.Powers
{
    public class PTruthChains : Power
    {
        // Powers-POCO v2: server logic lives in TruthChainsDecision (pure, EditMode-tested, both branches).
        // The selection flow + RPC plumbing stay here; the RPC body just triggers the decision + dispatch.
        private readonly TruthChainsDecision _decision = new();

        // Lane C (NGO-spawned): resolve the dependency ONCE in OnNetworkSpawn from the one
        // allowed static, store it in a field, and never look it up again
        // (refactor-architecture-despaghetti.md §3 lane C).
        private CharacterManager _characterManager;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _characterManager = CompositionRoot.For(NetworkManager).CharacterManager;
            Assert.IsNotNull(_characterManager,
                "PTruthChains._characterManager unresolved — CompositionRoot.For(NetworkManager) returned no CharacterManager.");
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
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