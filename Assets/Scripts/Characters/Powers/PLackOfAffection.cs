#region

using System;
using ChatSystem;
using Extensions;
using FMODUnity;
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
    public class PLackOfAffection : Power
    {
        public EventReference onContactedAsChosenSound;
        public EventReference onContactedAsMarginalSound;
        public EventReference onContactedAsAnomalySound;
        
        private void OnPlayerContactedRpc(ulong _senderClientId)
        {
            
            Character _localCharacter = GameManager.instance.GetLocalCharacter(false);
            if (_localCharacter.role.factionType == FactionType.chosen)
            {
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    _senderClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal);
            }
            
            ChatManager.instance.AddMessageLocal("L'orpheline est venue vous voir...", GameValues.CHAT_SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);

            switch (_localCharacter.role.factionType)
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
        
        private void OnCardClicked(Card _clickedCard)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId, _clickedCard.characterInfo.ownerClientId);
            OnUsed();
            GameManager.instance.DoPowerMethodRpc(ownerClientId,
                this, nameof(OnPlayerContactedRpc),
                new [] { new NetworkSerializableObject(ownerClientId) },
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

        public override void NetworkSerialize<T>(BufferSerializer<T> _serializer)
        {
            base.NetworkSerialize(_serializer);
            onContactedAsChosenSound.NetworkSerialize(_serializer);
            onContactedAsMarginalSound.NetworkSerialize(_serializer);
            onContactedAsAnomalySound.NetworkSerialize(_serializer);
        }
    }
}