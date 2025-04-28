using System;
using GameLogic;
using UI.SelectPanels;
using UnityEngine;

namespace Characters.Powers
{
    [Serializable]
    public class PEmbraceOfShadows : Power
    {
        private void OnCardClicked(Card _clickedCard)
        {
            _clickedCard.characterInfo.CorruptPlayer();
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _clickedCard.characterInfo.ownerClientId, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
            OnUsed();
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

        public override void Use()
        {
            base.Use();
            BoardManager.instance.onCardClicked += OnCardClicked;
        }

        public override void OnUsed()
        {
            base.OnUsed();
            BoardManager.instance.onCardClicked -= OnCardClicked;
        }

        public override void Cancel()
        {
            if (!isCurrentlyUsed)
            {
                return;
            }
            base.Cancel();
            BoardManager.instance.onCardClicked -= OnCardClicked;
        }
    }
}
