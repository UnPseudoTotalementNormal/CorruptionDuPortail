#region

using System;
using FocusSystem;
using GameLogic;
using RoleTarget;
using FocusType = FocusSystem.FocusType;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PEmbraceOfShadows : Power
    {
        [NonSerialized] private Character clickedCharacter;
        
        private void OnCardClicked(Card _clickedCard)
        {
            clickedCharacter = _clickedCard.characterInfo;
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Roles);
            FocusManager.instance.FocusObject(_clickedCard.gameObject);
        }
        
        private void OnCharacterBarClicked(Character _character)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId, _character.ownerClientId);
            if (clickedCharacter.role.IsTheSameRole(_character.role))
            {
                clickedCharacter.CorruptPlayer();
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    clickedCharacter.ownerClientId, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
                GameManager.instance.gameInfoRevealer.SetRevealLevel(
                    clickedCharacter.ownerClientId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal);
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
    }
}
