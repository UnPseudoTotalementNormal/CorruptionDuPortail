using System;
using System.Linq;
using ArrowSystem;
using FocusSystem;
using GameLogic;
using UnityEngine;
using FocusType = FocusSystem.FocusType;

namespace Characters.Powers
{
    [Serializable]
    public class PCorruptingMark : Power
    {
        
        private void OnCardClicked(Card _clickedCard)
        {
            _clickedCard.characterInfo.CorruptPlayer();
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _clickedCard.characterInfo.ownerClientId, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
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
            BoardManager.instance.onCardClicked -= OnCardClicked;
            ArrowManager.instance.DestroyAllArrows();
            
            FocusManager.instance.UnfocusAll();
        }
    }
}
