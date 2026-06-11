using UnityEngine;
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

            OnCardClickedRpc(_character.ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void OnCardClickedRpc(ulong _targetClientId)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _targetClientId);
            var _targetCharacter = GameManager.For(NetworkManager).characterManager.GetCharacter(_targetClientId, false);
            if (_targetCharacter == null)
            {
                return;
            }

            if (_targetCharacter.role.factionType == FactionType.anomaly)
            {
                ChainingManager.instance.AddCharacterToChainingList(_targetClientId);
                ChatManager.instance.SendChatMessageServerRpc(
                    new ChatMessage(GameValues.CHAT_SERVER_CLIENT_ID,
                        $"{LobbyPlayerInfoHolder.instance.GetPlayerInfo(_targetClientId).playerName} sera lié par les chaînes de la vérité.",
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