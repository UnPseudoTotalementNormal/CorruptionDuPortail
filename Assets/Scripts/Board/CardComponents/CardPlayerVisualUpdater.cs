#region

using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using Characters;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UI.CardUI;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace Board.CardComponents
{
    [System.Serializable]
    public class CardPlayerVisualUpdater : ICardDisplay
    {
        private const float FADE_DURATION = 0.25f;
        
        private float chainFadeTime = 0.5f;
        private CardVisualComponents _visualComponents;
        private bool lastIsChainedStatus = false;

        public void Initialize(CardVisualComponents _visualComponents)
        {
            this._visualComponents = _visualComponents;
        }


        public void SetPseudo(string _pseudo)
        {
            _visualComponents.cardPlayerPseudo.text = _pseudo;
            _visualComponents.bsCardPlayerPseudo.text = _pseudo;
        }

        public void SetRoleText(string _roleText)
        {
            _visualComponents.cardRoleText.text = _roleText;
        }

        public void SetFaction(FactionType _factionType)
        {
            if (_visualComponents.factionLogo.ContainsKey(_factionType))
            {
                _visualComponents.factionLogoImage.sprite = _visualComponents.factionLogo[_factionType];
                _visualComponents.bsFactionLogoImage.sprite = _visualComponents.factionLogoImage.sprite;
            }
            else
            {
                _visualComponents.factionLogoImage.sprite = null;
                _visualComponents.bsFactionLogoImage.sprite = null;
            }

            if (_visualComponents.factionLogoBackground.ContainsKey(_factionType))
            {
                _visualComponents.factionLogoBackgroundImage.sprite = _visualComponents.factionLogoBackground[_factionType];
            }
            else
            {
                _visualComponents.factionLogoBackgroundImage.sprite = null;
            }
            
            bool _factionActive = _visualComponents.factionLogoImage.sprite != null;
            _visualComponents.factionLogoImage.gameObject.SetActive(_factionActive);
            _visualComponents.bsFactionLogoImage.gameObject.SetActive(_factionActive);
            _visualComponents.factionLogoBackgroundImage.gameObject.SetActive(_factionActive);
            _visualComponents.unknownFogOverlay.gameObject.SetActive(_factionType == FactionType.unknown);
        }

        public void SetUnknown()
        {
            SetPseudo("");
            SetRoleText("");
            _visualComponents.cardImage.sprite = _visualComponents.unknownCardSprite;
            SetFaction(FactionType.unknown);
        }

        public void SetUnknownWithPseudo(string _pseudo)
        {
            SetPseudo(_pseudo);
            SetRoleText("");
            _visualComponents.cardImage.sprite = _visualComponents.unknownCardSprite;
            SetFaction(FactionType.unknown);
        }

        public void SetChainedOverlay(bool _isChained, bool _instant = false)
        {
            if (lastIsChainedStatus == _isChained)
            {
                return;
            }
            
            _visualComponents.chainedOverlay.DOFade(_isChained ? 1 : 0, _instant ? 0 : chainFadeTime);
            lastIsChainedStatus = _isChained;
        }

        public async UniTask SetRolePortrait(Role _role)
        {
            _visualComponents.cardImage.sprite = await _role.GetRolePortrait();
        }

        public void ShowFrontSideInfo(Transform _voteCanvasTransform, Transform _cardEffectsParent)
        {
            _voteCanvasTransform.localRotation = Quaternion.Euler(0, 0, 0);
            _cardEffectsParent.localRotation = Quaternion.Euler(0, 0, 0);
            
            FadeCanvasGroups(_visualComponents.objectsToShowOnFrontSidePlacementOnly, 1);
            FadeCanvasGroups(_visualComponents.objectsToShowOnBackSidePlacementOnly, 0);
        }

        public void ShowBackSideInfo(Transform _voteCanvasTransform, Transform _cardEffectsParent)
        {
            const float BACK_ROTATION_Y = 180f;
            const float BACK_ROTATION_Z = 180f;
            
            _voteCanvasTransform.localRotation = Quaternion.Euler(0, BACK_ROTATION_Y, 0);
            _cardEffectsParent.localRotation = Quaternion.Euler(0, 0, BACK_ROTATION_Z);
            
            FadeCanvasGroups(_visualComponents.objectsToShowOnFrontSidePlacementOnly, 0);
            FadeCanvasGroups(_visualComponents.objectsToShowOnBackSidePlacementOnly, 1);
        }
        
        public void SetMeIconActive(bool _isActive) { _visualComponents.meIconCard.gameObject.SetActive(_isActive); }

        private void FadeCanvasGroups(List<CanvasGroup> _canvasGroups, float _targetAlpha)
        {
            foreach (CanvasGroup _canvasGroup in _canvasGroups)
            {
                _canvasGroup.DOKill();
                _canvasGroup.DOFade(_targetAlpha, FADE_DURATION);
            }
        }
    }
}

