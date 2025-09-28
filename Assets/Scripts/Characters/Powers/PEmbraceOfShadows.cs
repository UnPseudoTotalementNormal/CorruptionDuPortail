#region

using System;
using Characters.Powers.Interfaces;
using Characters.Powers.Target;
using Extensions;
using FMODUnity;
using FocusSystem;
using GameLogic;
using Network;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;
using FocusType = FocusSystem.FocusType;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PEmbraceOfShadows : Power, ICorruptionChainPower
    {
        [NonSerialized] private Character clickedCharacter;
        public EventReference onCorruptionSuccessfulSound;
        public EventReference onCorruptionFailedSound;
        
        public event Action<Character> onCharacterCorruptionSuccessful;
        public event Action<Character> onCharacterCorruptionFailed;
        [field:SerializeField] public int maxCorruptionChain { get; set; } = 2;
        public int currentCorruptionChain { get; set; }

        public void InvokeOnCharacterCorrupted(ulong characterId)
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
                return;
            }
            
            clickedCharacter = _clickedCard.characterInfo;
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Roles, targetIncludeFlags);
            FocusManager.instance.FocusObject(_clickedCard.gameObject);
        }
        
        private void OnCharacterBarClicked(Character _character)
        {
            if (!TargetUtils.GetTargetsForRoles(targetIncludeFlags).Contains(_character.ownerClientId.Value))
            {
                return;
            }
            
            RoleTargetSystem.instance.NewTargeting(ownerClientId, clickedCharacter.ownerClientId.Value);
            if (clickedCharacter.role.IsTheSameRole(_character.role))
            {
                clickedCharacter.CorruptPlayer();
                GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(InvokeOnCharacterCorrupted),
                    new NetworkSerializableObject[]{ new(clickedCharacter.ownerClientId.Value) }, new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    clickedCharacter.ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    clickedCharacter.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal);
                onCorruptionSuccessfulSound.TryPlayOneShot();
            }
            else
            {
                GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(InvokeOnCharacterCorruptionFailed),
                    new NetworkSerializableObject[]{ new(clickedCharacter.ownerClientId.Value) }, new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
                onCorruptionFailedSound.TryPlayOneShot();
            }
            OnUsed();
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
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards, targetIncludeFlags);

            clickedCharacter = null;
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
            GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
            FocusManager.instance.UnfocusAll();
        }

        public override void NetworkSerialize<T>(BufferSerializer<T> _serializer)
        {
            base.NetworkSerialize(_serializer);

            var _tempCorruptionChain = maxCorruptionChain;
            _serializer.SerializeValue(ref _tempCorruptionChain);
            maxCorruptionChain = _tempCorruptionChain;
            
            var _tempCurrentCorruptionChain = currentCorruptionChain;
            _serializer.SerializeValue(ref _tempCurrentCorruptionChain);
            currentCorruptionChain = _tempCurrentCorruptionChain;
            
            onCorruptionFailedSound.NetworkSerialize(_serializer);
            onCorruptionSuccessfulSound.NetworkSerialize(_serializer);
        }
    }
}
