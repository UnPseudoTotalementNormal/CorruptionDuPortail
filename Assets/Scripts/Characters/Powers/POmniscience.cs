using System;
using Characters.Powers.Target;
using FocusSystem;
using GameLogic;
using Network;
using RoleTarget;
using Unity.Netcode;

namespace Characters.Powers
{
    [Serializable]
    public class POmniscience : Power
    {
        public ulong hackedCharacterClientId = HACKED_CHARACTER_DEFAULT;

        public const ulong HACKED_CHARACTER_DEFAULT = 4994996541621;
        
        private void OnCardClicked(Card _clickedCard)
        {
            var _character = GameManager.instance.GetCharacter(_clickedCard.characterInfo.ownerClientId);
            
            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(_character.ownerClientId))
            {
                return;
            }
            
            RoleTargetSystem.instance.NewTargeting(ownerClientId, _clickedCard.characterInfo.ownerClientId);

            GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(OnCardClickedRpc), 
                new[] { new NetworkSerializableObject(_character.ownerClientId) }, new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
            
            OnUsed();
        }
        
        private void OnCardClickedRpc(ulong _targetClientId)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId, ownerClientId);
            
            hackedCharacterClientId = _targetClientId;

            GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(_targetClientId,
                nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, true,
                NetworkManager.Singleton.RpcTarget.Single(ownerClientId, RpcTargetUse.Persistent));
            
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
            _serializer.SerializeValue(ref hackedCharacterClientId);
        }
    }
}