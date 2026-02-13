using System;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using Characters;
using Cysharp.Threading.Tasks;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Board.CardComponents
{
    [Serializable]
    public class CardRoleVisualUpdater : ICardDisplay
    {
        private CardVisualComponents _visualComponents;

        public void Initialize(CardVisualComponents _visualComponents)
        {
            this._visualComponents = _visualComponents;
            
            // Initialize role card specific settings
            if (this._visualComponents != null)
            {
                this._visualComponents.cardPlayerPseudo.gameObject.SetActive(false);
                this._visualComponents.unknownFogOverlay.gameObject.SetActive(false);
                this._visualComponents.chainedOverlay.gameObject.SetActive(false);
            }
        }
        
        public void SetPseudo(string _pseudo)
        {
            Debug.LogWarning("SetPseudo should not be called as this is a role card");
        }

        public void SetRoleText(string _roleText)
        {
            _visualComponents.cardRoleText.text = _roleText;
        }
        
        public void SetUnknownWithPseudo(string _pseudo)
        {
            Debug.LogWarning("SetUnknownWithPseudo should not be called as this is a role card");
        }

        public void SetFaction(FactionType _factionType)
        {
            if (_visualComponents.factionLogo.ContainsKey(_factionType))
            {
                _visualComponents.factionLogoImage.sprite = _visualComponents.factionLogo[_factionType];
            }
            else
            {
                _visualComponents.factionLogoImage.sprite = null;
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
            _visualComponents.factionLogoBackgroundImage.gameObject.SetActive(_factionActive);
            _visualComponents.unknownFogOverlay.gameObject.SetActive(_factionType == FactionType.unknown);
        }

        public void SetUnknown()
        {
            Debug.LogWarning("SetUnknown should not be called as this is a role card");
        }

        public void SetChainedOverlay(bool _isChained, bool _instant = false)
        {
            Debug.LogWarning("SetChainedOverlay should not be called as this is a role card");
        }

        public async UniTask SetRolePortrait(Role _role)
        {
            _visualComponents.cardImage.sprite = await _role.GetRolePortrait();
        }
        
        public void ShowFrontSideInfo(Transform _voteCanvasTransform, Transform _cardEffectsParent)
        {
            // Role cards don't have front/back side rotation logic
        }

        public void ShowBackSideInfo(Transform _voteCanvasTransform, Transform _cardEffectsParent)
        {
            // Role cards don't have front/back side rotation logic
        }
    }
}