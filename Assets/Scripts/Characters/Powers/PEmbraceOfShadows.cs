using System;
using System.Linq;
using ArrowSystem;
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

        public override void StartUse()
        {
            base.StartUse();
            BoardManager.instance.onCardClicked += OnCardClicked;

            ArrowManager.instance.StartNewArrow(
                GameManager.instance.powersBar.GetPowerBarObject(this).transform.position,
                Input.mousePosition);
        }

        public override void OnUsed()
        {
            base.OnUsed();
            BoardManager.instance.onCardClicked -= OnCardClicked;
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
                foreach (var _hit in _results)
                {
                    var _card = _hit.transform.GetComponentInParent<Card>();
                    if (_card)
                    {
                        _foundPosition = _card.transform.position;
                        break;
                    }
                }
                
                _arrow.SetPointB(_foundPosition);
            }
        }
    }
}
