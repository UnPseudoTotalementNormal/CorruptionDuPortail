#region

using System;
using Characters.Powers.Target;
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
        
        private void OnCardClicked(Card _clickedCard)
        {
            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(_clickedCard.characterInfo.ownerClientId.Value))
            {
                return;
            }
            
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _clickedCard.characterInfo.ownerClientId.Value);
            OnUsed();
            OnPlayerContactedRpc(_clickedCard.characterInfo.ownerClientId.Value, ownerClientId.Value, NetworkManager.RpcTarget.Single(_clickedCard.characterInfo.ownerClientId.Value, RpcTargetUse.Persistent));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void OnPlayerContactedRpc(ulong targetClientId, ulong senderClientId, RpcParams rpcParams = default)
        {
            if (NetworkManager.Singleton.LocalClientId != targetClientId) return;
            Character _localCharacter = GameManager.instance.characterManager.GetLocalCharacter(false);
            Character _senderCharacter = GameManager.instance.characterManager.GetCharacter(senderClientId, false);
            if (_localCharacter.role.factionType == FactionType.chosen)
            {
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    senderClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal);
            }
            ChatManager.instance.AddMessageLocal($"{_senderCharacter.role.roleName} est venu(e) vous voir...", GameValues.CHAT_SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
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