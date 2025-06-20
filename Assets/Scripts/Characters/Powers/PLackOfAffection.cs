#region

using System;
using ChatSystem;
using FocusSystem;
using GameLogic;
using Network;
using Unity.Netcode;
using FocusType = FocusSystem.FocusType;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PLackOfAffection : Power
    {
        private static void OnPlayerContactedRpc(ulong _senderClientId)
        {
            Character _localCharacter = GameManager.instance.GetLocalCharacter(false);
            if (_localCharacter.role.factionType == FactionType.chosen)
            {
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    _senderClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal);
            }
            
            ChatManager.instance.AddMessageLocal("L'orpheline est venue vous voir...", GameValues.SERVER_CLIENT_ID);
        }
        
        private void OnCardClicked(Card _clickedCard)
        {
            OnUsed();
            GameManager.instance.DoPowerStaticMethodRpc(
                typeof(PLackOfAffection).FullName, nameof(OnPlayerContactedRpc),
                new [] { new NetworkSerializableObject(NetworkManager.Singleton.LocalClientId) },
                new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new [] { _clickedCard.characterInfo.ownerClientId }));
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
    }
}