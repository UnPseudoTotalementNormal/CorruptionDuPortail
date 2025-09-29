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

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PHighPriorityBounty : Power
    {
        public RoleID targetRoleID = RoleID.Robot;
        
        private void OnCardClicked(Card _clickedCard)
        {
            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(_clickedCard.characterInfo.ownerClientId.Value))
            {
                return;
            }
            
            RoleTargetSystem.instance.NewTargeting(ownerClientId, _clickedCard.characterInfo.ownerClientId.Value);

            var _character = GameManager.instance.characterManager.GetCharacter(_clickedCard.characterInfo.ownerClientId.Value);
            GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(OnCardClickedRpc), 
                new[] { new NetworkSerializableObject(_character.ownerClientId.Value) }, new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
            
            OnUsed();
        }
        
        private void OnCardClickedRpc(ulong _targetClientId)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId, ownerClientId);
            
            var _characterTarget = GameManager.instance.characterManager.GetCharacter(_targetClientId);
            var _characterOwner = GameManager.instance.characterManager.GetCharacter(ownerClientId);
            if (_characterTarget.role.roleID == RoleID.Robot)
            {
                _characterTarget.isEliminated.Value = true;
                string _characterPseudo = LobbyPlayerInfoHolder.instance.GetPlayerInfo(_targetClientId).playerName.ToString();
                ChatManager.instance.SendChatMessageServerRpc(
                    new ChatMessage(GameValues.CHAT_SERVER_CLIENT_ID,
                        $"{_characterPseudo} était le robot et a été éliminé par {_characterOwner.role.roleName}.", 
                        (int)ChatWindowIDs.Server));
                GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(_targetClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Public, true);
                //TODO: do actual elimination logic & visual
            }
            else
            {
                GameManager.instance.chainingManager.chainingPlayers.Add(_characterOwner.ownerClientId.Value);
                ChatManager.instance.ReceiveChatMessageRpc(
                    new ChatMessage(GameValues.CHAT_SERVER_CLIENT_ID,
                        $"Votre cible n'était pas le robot. Vous serez enchaîné à la fin de l'éveil.",
                        (int)ChatWindowIDs.Server));
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
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards, targetIncludeFlags);
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