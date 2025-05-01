using System;
using System.Linq;
using ArrowSystem;
using GameLogic;
using UnityEngine;

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

            ArrowManager.instance.StartNewArrow(
                GameManager.instance.powersBar.GetPowerBarObject(this).transform.position,
                Input.mousePosition);

            hoveredCard = null;
        }

        public override void OnUsed()
        {
            base.OnUsed();
            BoardManager.instance.onCardClicked -= OnCardClicked;
            BoardManager.instance.onCardHovered -= OnCardHovered;
            BoardManager.instance.onCardUnhovered -= OnCardUnhovered;
            ArrowManager.instance.DestroyAllArrows();
        }

        public override void Cancel()
        {
            if (!isCurrentlyUsed)
            {
                return;
            }
            base.Cancel();
            BoardManager.instance.onCardClicked -= OnCardClicked;
            BoardManager.instance.onCardHovered -= OnCardHovered;
            BoardManager.instance.onCardUnhovered -= OnCardUnhovered;
            ArrowManager.instance.DestroyAllArrows();
        }

        public override void UsingPowerUpdate()
        {
            ArrowObject _arrow = ArrowManager.instance.GetLastArrow();
            if (_arrow)
            {
                Ray _ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit[] _results = Physics.RaycastAll(_ray.origin, _ray.direction, Mathf.Infinity);
                Vector3 _foundPosition = _results.First(_hit => _hit.collider.gameObject.layer == LayerMask.NameToLayer("Arrow")).point;

                if (hoveredCard)
                {
                    _foundPosition = hoveredCard.transform.position;
                }
                
                _arrow.SetPointB(_foundPosition);
            }
        }
    }
}
