using FocusSystem;
using GameLogic;

namespace Characters.Powers
{
    public class PCursedVision : Power
    {
        private void OnCardClicked(Card _clickedCard)
        {
            var _character = _clickedCard.characterInfo;
            _character.CorruptPlayer();
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _character.ownerClientId, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
            if (_character.role.factionType == FactionType.chosen)
            {
                ChatSystem.ChatManager.instance.AddMessageLocal($"{_character.GetOwnerPseudo()} est un élu.", GameValues.SERVER_CLIENT_ID);
            }
            else
            {
                ChatSystem.ChatManager.instance.AddMessageLocal($"{_character.GetOwnerPseudo()} n'est pas un élu.", GameValues.SERVER_CLIENT_ID);
            }
            
            GameManager.instance.GetCharacter(ownerClientId).CorruptPlayer();   
            GameManager.instance.gameInfoRevealer.SetRevealLevel(ownerClientId, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
            
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
    }
}
