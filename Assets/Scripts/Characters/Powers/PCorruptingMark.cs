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
        public event Action<Character> onCharacterCorruptionSuccessful;
        public event Action<Character> onCharacterCorruptionFailed;

        public void InvokeOnCharacterCorruptionSuccessful(Character _character)
        {
            onCharacterCorruptionSuccessful?.Invoke(_character);
        }
        public void InvokeOnCharacterCorruptionFailed(Character _character)
        {
            onCharacterCorruptionFailed?.Invoke(_character);
        }
        private void OnCardClicked(Card _clickedCard)
        {
            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(_clickedCard.characterInfo.ownerClientId.Value))
            {
                GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(InvokeOnCharacterCorruptionFailed),
                    new NetworkSerializableObject[]{ new(_clickedCard.characterInfo)}, new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
                return;
            }
            
            RoleTargetSystem.instance.NewTargeting(ownerClientId, _clickedCard.characterInfo.ownerClientId.Value);
            _clickedCard.characterInfo.CorruptPlayer();
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _clickedCard.characterInfo.ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
            GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(InvokeOnCharacterCorruptionSuccessful),
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
