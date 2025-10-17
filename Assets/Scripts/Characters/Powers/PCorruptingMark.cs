#region

using System;
using ArrowSystem;
using Characters.Powers.Interfaces;
using Characters.Powers.Target;
using FocusSystem;
using GameLogic;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;
using FocusType = FocusSystem.FocusType;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PCorruptingMark : Power, ICorrupterPower, IConcentratedPowerEffect
    {
        [field:SerializeField] public string concentratedEffectDescription { get; set; }
        
        private NetworkVariable<ulong> lastCorruptedCharacterId = new(9999999);
        
        public event Action<Character> onCharacterCorruptionSuccessful;
        public event Action<Character> onCharacterCorruptionFailed;

        public void InvokeOnCharacterCorruptionSuccessful(ulong characterId)
        {
            var _character = GameManager.instance.characterManager.GetCharacter(characterId);
            onCharacterCorruptionSuccessful?.Invoke(_character);
        }
        public void InvokeOnCharacterCorruptionFailed(ulong characterId)
        {
            var _character = GameManager.instance.characterManager.GetCharacter(characterId);
            onCharacterCorruptionFailed?.Invoke(_character);
        }
        private void OnCardClicked(Card _clickedCard)
        {
            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(_clickedCard.characterInfo.ownerClientId.Value))
            {
                InvokeOnCharacterCorruptionFailedRpc(_clickedCard.characterInfo.ownerClientId.Value);
                return;
            }
            lastCorruptedCharacterId.Value = _clickedCard.characterInfo.ownerClientId.Value;
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _clickedCard.characterInfo.ownerClientId.Value);
            _clickedCard.characterInfo.CorruptPlayerServerRpc();
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _clickedCard.characterInfo.ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
            InvokeOnCharacterCorruptionSuccessfulRpc(_clickedCard.characterInfo.ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Everyone)]
        private void InvokeOnCharacterCorruptionSuccessfulRpc(ulong characterId)
        {
            InvokeOnCharacterCorruptionSuccessful(characterId);
        }

        [Rpc(SendTo.Everyone)]
        private void InvokeOnCharacterCorruptionFailedRpc(ulong characterId)
        {
            InvokeOnCharacterCorruptionFailed(characterId);
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
            ArrowManager.instance.DestroyAllArrows();
            
            FocusManager.instance.UnfocusAll();
        }
        
        public void OnConcentratedEffectServer()
        {
            GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(
                lastCorruptedCharacterId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, true,
                NetworkManager.RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
        }
    }
}
