using UnityEngine;
using UnityEngine.Assertions;
using Characters.Powers.Target;
using ChatSystem;
using GameLogic;
using Network;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;

namespace Characters.Powers
{
    public class PTruthChains : Power
    {
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
            roleTargetSystem.NewTargeting(ownerClientId.Value, _targetClientId);
            var _targetCharacter = _characterManager.GetCharacter(_targetClientId, false);
            if (_targetCharacter == null)
            {
                return;
            }

            if (_targetCharacter.role.factionType == FactionType.anomaly)
            {
                chainingManager.AddCharacterToChainingList(_targetClientId);
                chatManager.SendChatMessageServerRpc(
                    new ChatMessage(GameValues.CHAT_SERVER_CLIENT_ID,
                        $"{lobbyPlayerInfoHolder.GetPlayerInfo(_targetClientId).playerName} sera lié par les chaînes de la vérité.",
                        (int)ChatWindowIDs.Server));
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