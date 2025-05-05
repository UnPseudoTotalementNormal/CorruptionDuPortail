using System;
using System.Linq;
using FocusSystem;
using GameLogic;
using Network;
using Unity.Netcode;
using UnityEngine;
using FocusType = FocusSystem.FocusType;

namespace Characters.Powers
{
    [Serializable]
    public class PLackOfAffection : Power
    {
        private static void OnPlayerContactedRpc(ulong _senderClientId)
        {
            Character _localCharacter = GameManager.instance.GetLocalCharacter(false);
            if (_localCharacter.role.factionType != FactionType.anomaly)
            {
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    _senderClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal);
            }
            else
            {
                //TODO: Feedback
            }
        }
        
        private void OnCardClicked(Card _clickedCard)
        {
            OnUsed();
            GameManager.instance.DoPowerStaticMethodRpc(
                NetworkManager.Singleton.LocalClientId, typeof(PLackOfAffection).FullName, nameof(OnPlayerContactedRpc),
                new [] { new NetworkSerializableObject(NetworkManager.Singleton.LocalClientId) },
                new CustomRpcParams(CustomRpcParams.RpcTargetType.single, new [] { _clickedCard.characterInfo.ownerClientId }));
        }
        
        public override bool CanUse()
        {
            bool _baseValue = base.CanUse();
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
            BoardManager.instance.onCardClicked -= OnCardClicked;
            
            FocusManager.instance.UnfocusAll();
        }
    }
}