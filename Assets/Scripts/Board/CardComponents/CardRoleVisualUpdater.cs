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
        private CardVisualComponents visualComponents;

        public void Initialize(CardVisualComponents _visualComponents)
        {
            visualComponents = _visualComponents;

            if (this.visualComponents == null)
            {
                return;
            }
            
            visualComponents.meIconCard.gameObject.SetActive(false);
            visualComponents.cardPlayerPseudo.gameObject.SetActive(false);
            visualComponents.unknownFogOverlay.gameObject.SetActive(false);
            visualComponents.chainedOverlay.gameObject.SetActive(false);
            visualComponents.cardPlayerPseudoHolder.gameObject.SetActive(false);

            RectTransform roleTextRectTransform = visualComponents.cardRoleTextHolder.GetComponent<RectTransform>();
            roleTextRectTransform.localPosition = new Vector3(
                roleTextRectTransform.localPosition.x, 
                -roleTextRectTransform.localPosition.y,
                roleTextRectTransform.localPosition.z
                );
            SetRoleText(_visualComponents.card.roleInfo.roleName.ToString());
            
            visualComponents.factionLogoImage.gameObject.SetActive(true);
            visualComponents.factionLogoImage.sprite = visualComponents.factionLogo[_visualComponents.card.roleInfo.factionType];
            
            SetRolePortrait(_visualComponents.card.roleInfo);
        }
        
        public void SetPseudo(string _pseudo)
        {
            Debug.LogWarning("SetPseudo should not be called as this is a role card");
        }

        public void SetRoleText(string _roleText)
        {
            visualComponents.cardRoleText.text = _roleText;
        }
        
        public void SetUnknownWithPseudo(string _pseudo)
        {
            Debug.LogWarning("SetUnknownWithPseudo should not be called as this is a role card");
        }

        public void SetFaction(FactionType _factionType)
        {
            if (visualComponents.factionLogo.ContainsKey(_factionType))
            {
                visualComponents.factionLogoImage.sprite = visualComponents.factionLogo[_factionType];
            }
            else
            {
                visualComponents.factionLogoImage.sprite = null;
            }

            if (visualComponents.factionLogoBackground.ContainsKey(_factionType))
            {
                visualComponents.factionLogoBackgroundImage.sprite = visualComponents.factionLogoBackground[_factionType];
            }
            else
            {
                visualComponents.factionLogoBackgroundImage.sprite = null;
            }
            
            bool _factionActive = visualComponents.factionLogoImage.sprite != null;
            visualComponents.factionLogoImage.gameObject.SetActive(_factionActive);
            visualComponents.factionLogoBackgroundImage.gameObject.SetActive(_factionActive);
            visualComponents.unknownFogOverlay.gameObject.SetActive(_factionType == FactionType.unknown);
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
            visualComponents.cardImage.sprite = await _role.GetRolePortrait();
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