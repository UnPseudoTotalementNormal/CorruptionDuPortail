#region

using System;
using Extensions;
using FMODUnity;
using FocusSystem;
using GameLogic;
using RoleTarget;
using Unity.Netcode;
using FocusType = FocusSystem.FocusType;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PEmbraceOfShadows : Power
    {
        [NonSerialized] private Character clickedCharacter;
        public EventReference onCorruptionSuccessfulSound;
        public EventReference onCorruptionFailedSound;
        
        private void OnCardClicked(Card _clickedCard)
        {
            clickedCharacter = _clickedCard.characterInfo;
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Roles);
            FocusManager.instance.FocusObject(_clickedCard.gameObject);
        }
        
        private void OnCharacterBarClicked(Character _character)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId, clickedCharacter.ownerClientId);
            if (clickedCharacter.role.IsTheSameRole(_character.role))
            {
                clickedCharacter.CorruptPlayer();
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    clickedCharacter.ownerClientId, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    clickedCharacter.ownerClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal);
                onCorruptionSuccessfulSound.TryPlayOneShot();
            }
            else
            {
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
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards);

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
            
            onCorruptionFailedSound.NetworkSerialize(_serializer);
            onCorruptionSuccessfulSound.NetworkSerialize(_serializer);
        }
    }
}
