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
        [NonSerialized] private Card hoveredCard;
        
        private void OnCardClicked(Card _clickedCard)
        {
            _clickedCard.characterInfo.CorruptPlayer();
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _clickedCard.characterInfo.ownerClientId, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
            OnUsed();
        }
        
        private void OnCardUnhovered(Card _unhoveredCard)
        {
            hoveredCard = null;
        }

        private void OnCardHovered(Card _hoveredCard)
        {
            hoveredCard = _hoveredCard;
        }
        
        public override bool CanUse()
        {
            bool _baseValue = base.CanUse();
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
            BoardManager.instance.onCardHovered += OnCardHovered;
            BoardManager.instance.onCardUnhovered += OnCardUnhovered;

            hoveredCard = null;
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards);
        }

        public override void OnUsed()
        {
            base.OnUsed();
            StopUse();
        }

        public override void Cancel()
        {
            if (!isCurrentlyUsed)
            {
                return;
            }
            base.Cancel();
            StopUse();
        }
        
        private void StopUse()
        {
            BoardManager.instance.onCardClicked -= OnCardClicked;
            BoardManager.instance.onCardHovered -= OnCardHovered;
            BoardManager.instance.onCardUnhovered -= OnCardUnhovered;
            ArrowManager.instance.DestroyAllArrows();
            
            FocusManager.instance.UnfocusAll();
        }
    }
}
