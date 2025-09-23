#region

using System;
using ArrowSystem;
using Characters.Powers.Interfaces;
using Characters.Powers.Target;
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
    public class PCorruptingMark : Power, ICorrupterPower
    {
        public event Action<Character> onCharacterCorrupted;
        public void InvokeOnCharacterCorrupted(Character _character)
        {
            onCharacterCorrupted?.Invoke(_character);
        }
        private void OnCardClicked(Card _clickedCard)
        {
            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(_clickedCard.characterInfo.ownerClientId))
            {
                return;
            }
            
            RoleTargetSystem.instance.NewTargeting(ownerClientId, _clickedCard.characterInfo.ownerClientId);
            _clickedCard.characterInfo.CorruptPlayer();
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _clickedCard.characterInfo.ownerClientId, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
            GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(InvokeOnCharacterCorrupted),
                new NetworkSerializableObject[]{ new(_clickedCard.characterInfo)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
            OnUsed();
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
            ArrowManager.instance.DestroyAllArrows();
            
            FocusManager.instance.UnfocusAll();
        }
    }
}
