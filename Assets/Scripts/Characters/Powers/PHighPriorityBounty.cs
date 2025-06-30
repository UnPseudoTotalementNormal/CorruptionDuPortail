#region

using System;
using ChatSystem;
using FocusSystem;
using GameLogic;
using Network;
using RoleTarget;
using Unity.Netcode;
using FocusType = FocusSystem.FocusType;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PHighPriorityBounty : Power
    {
        public RoleID targetRoleID = RoleID.Robot;
        
        private void OnCardClicked(Card _clickedCard)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId, _clickedCard.characterInfo.ownerClientId);

            var _character = GameManager.instance.GetCharacter(_clickedCard.characterInfo.ownerClientId);
            GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(OnCardClickedRpc), 
                new[] { new NetworkSerializableObject(_character.ownerClientId) }, new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
            
            OnUsed();
        }
        
        private void OnCardClickedRpc(ulong _targetClientId)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId, ownerClientId);
            
            var _characterTarget = GameManager.instance.GetCharacter(_targetClientId);
            var _characterOwner = GameManager.instance.GetCharacter(ownerClientId);
            if (_characterTarget.role.roleID == RoleID.Robot)
            {
                _characterTarget.isEliminated = true;
                string _characterPseudo = LobbyPlayerInfoHolder.instance.GetPlayerInfo(_targetClientId).playerName.ToString();
                ChatManager.instance.SendChatMessageServerRpc(
                    new ChatMessage(GameValues.CHAT_SERVER_CLIENT_ID, 
                        $"{_characterPseudo} était le robot et a été éliminé par {_characterOwner.role.roleName}."),
                        0);
                GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(_targetClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Public, true);
                //TODO: do actual elimination logic & visual
            }
            else
            {
                _characterOwner.isChained = true;
                BoardManager.instance.UpdateCardChainStatusRpc(_characterOwner.ownerClientId, false);
            }
            
            GameManager.instance.AskForUpdateAllCharactersRpc();
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
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards);
        }

        public override void OnUsed()
        {
            base.OnUsed();
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

        public override void NetworkSerialize<T>(BufferSerializer<T> _serializer)
        {
            base.NetworkSerialize(_serializer);
            _serializer.SerializeValue(ref targetRoleID);
        }
    }
}