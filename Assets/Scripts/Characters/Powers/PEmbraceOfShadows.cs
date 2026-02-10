#region

using System;
using Characters.Powers.Interfaces;
using Characters.Powers.Target;
using Extensions;
using FMODUnity;
using FocusSystem;
using GameLogic;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;
using FocusType = FocusSystem.FocusType;
using Board;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PEmbraceOfShadows : Power, IFailablePower
    {
        [NonSerialized] private Character clickedCharacter;
        public EventReference onCorruptionSuccessfulSound;
        public EventReference onCorruptionFailedSound;
        public event Action onPowerSuccessful;
        public event Action onPowerFailed;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }
        
        private void OnCardClicked(Card _clickedCard)
        {
            if (!CheckIsTargetValid(_clickedCard.characterInfo.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            clickedCharacter = _clickedCard.characterInfo;
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            FocusManager.instance.SetFocusOnType(FocusType.Roles, id => CheckIsTargetValid(id, TargetUtils.TargetType.Role));
            FocusManager.instance.FocusObject(_clickedCard.gameObject);
        }
        private void OnCharacterBarClicked(Character _character)
        {
            if (!CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Role))
            {
                return;
            }
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, clickedCharacter.ownerClientId.Value);
            if (clickedCharacter.role.IsTheSameRole(_character.role))
            {
                clickedCharacter.CorruptPlayerServerRpc();
                InvokeOnCharacterCorruptedRpc(clickedCharacter.ownerClientId.Value);
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    clickedCharacter.ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    clickedCharacter.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal);
                onCorruptionSuccessfulSound.TryPlayOneShot();
            }
            else
            {
                InvokeOnCharacterCorruptionFailedRpc(clickedCharacter.ownerClientId.Value);
                onCorruptionFailedSound.TryPlayOneShot();
            }
            OnUsed();
        }

        [Rpc(SendTo.Everyone)]
        private void InvokeOnCharacterCorruptedRpc(ulong characterId)
        {
            onPowerSuccessful?.Invoke();
        }

        [Rpc(SendTo.Everyone)]
        private void InvokeOnCharacterCorruptionFailedRpc(ulong characterId)
        {
            onPowerFailed?.Invoke();
        }
        
        private void OnCorruptionSuccessful() //TODO : THIS
        {
            
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
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards, id => CheckIsTargetValid(id, TargetUtils.TargetType.Character));

            clickedCharacter = null;
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
            GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
            FocusManager.instance.UnfocusAll();
        }
    }
}
