#region

using System;
using Characters.Powers.Target;
using ChatSystem;
using FocusSystem;
using GameLogic;
using Network;
using RoleTarget;
using Unity.Netcode;
using FocusType = FocusSystem.FocusType;
using Board;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PHighPriorityBounty : Power
    {
        public RoleID targetRoleID = RoleID.Robot;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }
        
        private void OnCardClicked(Card _clickedCard)
        {
            if (!CheckIsTargetValid(_clickedCard.characterInfo.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _clickedCard.characterInfo.ownerClientId.Value);
            var _character = GameManager.instance.characterManager.GetCharacter(_clickedCard.characterInfo.ownerClientId.Value);
            OnCardClickedServerRpc(_character.ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void OnCardClickedServerRpc(ulong _targetClientId)
        {
            OnCardClickedRpc(_targetClientId);
        }
        
        private void OnCardClickedRpc(ulong _targetClientId)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, ownerClientId.Value);
            
            var _characterTarget = GameManager.instance.characterManager.GetCharacter(_targetClientId);
            var _characterOwner = GameManager.instance.characterManager.GetCharacter(ownerClientId.Value);
            if (_characterTarget.role.roleID == RoleID.Robot)
            {
                _characterTarget.isEliminated.Value = true;
                string _characterPseudo = LobbyPlayerInfoHolder.instance.GetPlayerInfo(_targetClientId).playerName.ToString();
                ChatManager.instance.ReceiveChatMessageRpc(
                    new ChatMessage(GameValues.CHAT_SERVER_CLIENT_ID,
                        $"{_characterPseudo} était le robot et a été éliminé par {_characterOwner.role.roleName}.", 
                        (int)ChatWindowIDs.Server));
                GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(_targetClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Public, true);
                //TODO: do actual elimination logic & visual
            }
            else
            {
                ChainingManager.instance.AddCharacterToChainingList(_characterOwner.ownerClientId.Value);
                ChatManager.instance.ReceiveChatMessageRpc(
                    new ChatMessage(GameValues.CHAT_SERVER_CLIENT_ID,
                        $"Votre cible n'était pas le robot. Vous serez enchaîné à la fin de l'éveil.",
                        (int)ChatWindowIDs.Server), RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
            }
            
            GameManager.instance.characterManager.AskForUpdateAllCharactersRpc();
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
            BoardManager.instance.onCardClicked += OnCardClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards, id => CheckIsTargetValid(id, TargetUtils.TargetType.Character));
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
            BoardManager.instance.onCardClicked -= OnCardClicked;
            
            FocusManager.instance.UnfocusAll();
        }
    }
}