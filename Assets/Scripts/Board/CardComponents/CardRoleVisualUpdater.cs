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
    public class CardRoleVisualUpdater : MonoBehaviour, ICardDisplay
    {
        [Header("Front Side References")]
        [SerializeField] private TMP_Text cardPlayerPseudo;
        [SerializeField] private TMP_Text cardRoleText;
        [SerializeField] private Image cardImage;
        [SerializeField] private Image factionLogoImage;
        [SerializeField] private Image factionLogoBackgroundImage;
        [SerializeField] private Image unknownFogOverlay;
        [SerializeField] private CanvasGroup chainedOverlay;
        [SerializeField] private List<CanvasGroup> objectsToShowOnFrontSidePlacementOnly = new();
        
        [SerializeField] private SerializedDictionary<FactionType, Sprite> factionLogo;
        [SerializeField] private SerializedDictionary<FactionType, Sprite> factionLogoBackground;

        private void Start()
        {
            cardPlayerPseudo.gameObject.SetActive(false);
            unknownFogOverlay.gameObject.SetActive(false);
            chainedOverlay.gameObject.SetActive(false);
        }

        public void SetPseudo(string _pseudo)
        {
            Debug.LogWarning("SetPseudo should not be called as this is a role card");
        }

        public void SetRoleText(string _roleText)
        {
            cardRoleText.text = _roleText;
        }
        
        public void SetUnknownWithPseudo(string _pseudo)
        {
            Debug.LogWarning("SetUnknownWithPseudo should not be called as this is a role card");
        }

        public void SetFaction(FactionType _factionType)
        {
            if (factionLogo.ContainsKey(_factionType))
            {
                factionLogoImage.sprite = factionLogo[_factionType];
            }
            else
            {
                factionLogoImage.sprite = null;
            }

            if (factionLogoBackground.ContainsKey(_factionType))
            {
                factionLogoBackgroundImage.sprite = factionLogoBackground[_factionType];
            }
            else
            {
                factionLogoBackgroundImage.sprite = null;
            }
            
            bool _factionActive = factionLogoImage.sprite != null;
            factionLogoImage.gameObject.SetActive(_factionActive);
            factionLogoBackgroundImage.gameObject.SetActive(_factionActive);
            unknownFogOverlay.gameObject.SetActive(_factionType == FactionType.unknown);
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
            cardImage.sprite = await _role.GetRolePortrait();
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