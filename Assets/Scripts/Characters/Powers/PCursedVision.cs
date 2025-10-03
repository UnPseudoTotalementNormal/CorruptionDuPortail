using System;
using Characters.Powers.Target;
using ChatSystem;
using FocusSystem;
using GameLogic;
using RoleTarget;
using Unity.Netcode;

namespace Characters.Powers
{
    [Serializable]
    public class PCursedVision : Power
    {
        private void OnCardClicked(Card _clickedCard)
        {
            var _character = _clickedCard.characterInfo;
            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(_character.ownerClientId.Value))
            {
                return;
            }
            RoleTargetSystem.instance.NewTargeting(ownerClientId, _character.ownerClientId.Value);
            _character.CorruptPlayer();
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _character.ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
            if (_character.role.factionType == FactionType.chosen)
            {
                ChatManager.instance.AddMessageLocal($"{_character.GetOwnerPseudo()} est un élu.", GameValues.CHAT_SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
            }
            else
            {
                ChatManager.instance.AddMessageLocal($"{_character.GetOwnerPseudo()} n'est pas un élu.", GameValues.CHAT_SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
            }
            GameManager.instance.characterManager.GetCharacter(ownerClientId).CorruptPlayer();   
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
    }
}
