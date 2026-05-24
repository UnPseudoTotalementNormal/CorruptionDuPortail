#region

using System;
using Characters.Powers.Target;
using ChatSystem;
using Extensions;
using FMODUnity;
using GameLogic;
using Network;
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
            OnUsed();
            OnPlayerContactedRpc(_character.ownerClientId.Value, ownerClientId.Value, CharacterManager.instance.GetSafeRpcTarget(_character.ownerClientId.Value));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void OnPlayerContactedRpc(ulong targetClientId, ulong senderClientId, RpcParams rpcParams = default)
        {
            if (!CharacterManager.instance.IsLocalOrSimulated(targetClientId)) return;
            
            Character _targetCharacter = GameManager.instance.characterManager.GetCharacter(targetClientId, false);
            Character _senderCharacter = GameManager.instance.characterManager.GetCharacter(senderClientId, false);
            
            if (_targetCharacter.role.factionType == FactionType.chosen)
            {
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    senderClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, targetClientId);
            }
            
            if (CharacterManager.instance.GetLocalClientId() == targetClientId)
            {
                ChatManager.instance.AddMessageLocal($"{_senderCharacter.role.roleName} est venu(e) vous voir...", GameValues.CHAT_SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
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

        public override void StartUse()
        {
            base.StartUse();
            SelectionFlowService.instance.StartCharacterSelection(targetValidator, OnCharacterPicked);
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