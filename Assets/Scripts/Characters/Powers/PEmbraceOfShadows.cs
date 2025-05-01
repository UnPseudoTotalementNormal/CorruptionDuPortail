using System;
using System.Linq;
using ArrowSystem;
using Board.UI.CharacterBar;
using GameLogic;
using UI.SelectPanels;
using UnityEngine;

namespace Characters.Powers
{
    [Serializable]
    public class PEmbraceOfShadows : Power
    {
        [NonSerialized] private Card hoveredCard;
        [NonSerialized] private CharactersBarObject charactersBarObject;
        
        [NonSerialized] private Character clickedCharacter;
        
        private void OnCardClicked(Card _clickedCard)
        {
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            GameManager.instance.charactersBar.onCharacterBarHovered += OnCharacterBarHovered;
            GameManager.instance.charactersBar.onCharacterBarUnhovered += OnCharacterBarUnhovered;
            ArrowManager.instance.CreateNewArrow(_clickedCard.transform.position, Vector3.zero);
            clickedCharacter = _clickedCard.characterInfo;
        }
        
        private void OnCardUnhovered(Card _unhoveredCard)
        {
            hoveredCard = null;
        }

        private void OnCardHovered(Card _hoveredCard)
        {
            hoveredCard = _hoveredCard;
        }
        
        private void OnCharacterBarClicked(Character _character)
        {
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
        
        private void OnCharacterBarUnhovered(Character _character)
        {
            charactersBarObject = null;
        }
        
        private void OnCharacterBarHovered(Character _character)
        {
            charactersBarObject = GameManager.instance.charactersBar.charactersBarObjects
                .Find(_obj => _obj.playerCharacter.ownerClientId == _character.ownerClientId);
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
            charactersBarObject = null;
        }

        public override void OnUsed()
        {
            base.OnUsed();
            BoardManager.instance.onCardClicked -= OnCardClicked;
            BoardManager.instance.onCardHovered -= OnCardHovered;
            BoardManager.instance.onCardUnhovered -= OnCardUnhovered;
            GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
            GameManager.instance.charactersBar.onCharacterBarHovered -= OnCharacterBarHovered;
            GameManager.instance.charactersBar.onCharacterBarUnhovered -= OnCharacterBarUnhovered;
            
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
            GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
            GameManager.instance.charactersBar.onCharacterBarHovered -= OnCharacterBarHovered;
            GameManager.instance.charactersBar.onCharacterBarUnhovered -= OnCharacterBarUnhovered;
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
                
                if (charactersBarObject)
                {
                    _foundPosition = charactersBarObject.transform.position;
                }
                
                _arrow.SetPointB(_foundPosition);
            }
        }
    }
}
