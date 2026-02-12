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
    /// <summary>
    /// Handles all visual updates of the card (texts, images, factions).
    /// Single responsibility: visual display of card information.
    /// </summary>
    public class CardPlayerVisualUpdater : MonoBehaviour, ICardDisplay
    {
        private const float FADE_DURATION = 0.25f;
        
        [Header("Both Side References")] 
        [SerializeField] private MeIconCard meIconCard;

        [Header("Front Side References")]
        [SerializeField] private TMP_Text cardPlayerPseudo;
        [SerializeField] private TMP_Text cardRoleText;
        [SerializeField] private Image cardImage;
        [SerializeField] private Image factionLogoImage;
        [SerializeField] private Image factionLogoBackgroundImage;
        [SerializeField] private Image unknownFogOverlay;
        [SerializeField] private CanvasGroup chainedOverlay;
        [SerializeField] private List<CanvasGroup> objectsToShowOnFrontSidePlacementOnly = new();

        [Header("Back Side References")]
        [SerializeField] private TMP_Text bsCardPlayerPseudo;
        [SerializeField] private Image bsFactionLogoImage;
        [SerializeField] private List<CanvasGroup> objectsToShowOnBackSidePlacementOnly = new();

        [Header("Assets")]
        [SerializeField] private Sprite unknownCardSprite;
        [SerializeField] private SerializedDictionary<FactionType, Sprite> factionLogo;
        [SerializeField] private SerializedDictionary<FactionType, Sprite> factionLogoBackground;

        [Header("Animation Settings")]
        [SerializeField] private float chainFadeTime = 0.5f;

        private bool lastIsChainedStatus = false;

        #region ICardDisplay Implementation

        public void SetPseudo(string _pseudo)
        {
            cardPlayerPseudo.text = _pseudo;
            bsCardPlayerPseudo.text = _pseudo;
        }

        public void SetRoleText(string _roleText)
        {
            cardRoleText.text = _roleText;
        }

        public void SetFaction(FactionType _factionType)
        {
            if (factionLogo.ContainsKey(_factionType))
            {
                factionLogoImage.sprite = factionLogo[_factionType];
                bsFactionLogoImage.sprite = factionLogoImage.sprite;
            }
            else
            {
                factionLogoImage.sprite = null;
                bsFactionLogoImage.sprite = null;
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
            bsFactionLogoImage.gameObject.SetActive(_factionActive);
            factionLogoBackgroundImage.gameObject.SetActive(_factionActive);
            unknownFogOverlay.gameObject.SetActive(_factionType == FactionType.unknown);
        }

        public void SetUnknown()
        {
            SetPseudo("");
            SetRoleText("");
            cardImage.sprite = unknownCardSprite;
            SetFaction(FactionType.unknown);
        }

        public void SetUnknownWithPseudo(string _pseudo)
        {
            SetPseudo(_pseudo);
            SetRoleText("");
            cardImage.sprite = unknownCardSprite;
            SetFaction(FactionType.unknown);
        }

        public void SetChainedOverlay(bool _isChained, bool _instant = false)
        {
            if (lastIsChainedStatus == _isChained)
            {
                return;
            }
            
            chainedOverlay.DOFade(_isChained ? 1 : 0, _instant ? 0 : chainFadeTime);
            lastIsChainedStatus = _isChained;
        }

        public async UniTask SetRolePortrait(Role _role)
        {
            cardImage.sprite = await _role.GetRolePortrait();
        }

        #endregion

        #region Side Display Methods

        public void ShowFrontSideInfo(Transform _voteCanvasTransform, Transform _cardEffectsParent)
        {
            _voteCanvasTransform.localRotation = Quaternion.Euler(0, 0, 0);
            _cardEffectsParent.localRotation = Quaternion.Euler(0, 0, 0);
            
            FadeCanvasGroups(objectsToShowOnFrontSidePlacementOnly, 1);
            FadeCanvasGroups(objectsToShowOnBackSidePlacementOnly, 0);
        }

        public void ShowBackSideInfo(Transform _voteCanvasTransform, Transform _cardEffectsParent)
        {
            const float BACK_ROTATION_Y = 180f;
            const float BACK_ROTATION_Z = 180f;
            
            _voteCanvasTransform.localRotation = Quaternion.Euler(0, BACK_ROTATION_Y, 0);
            _cardEffectsParent.localRotation = Quaternion.Euler(0, 0, BACK_ROTATION_Z);
            
            FadeCanvasGroups(objectsToShowOnFrontSidePlacementOnly, 0);
            FadeCanvasGroups(objectsToShowOnBackSidePlacementOnly, 1);
        }

        #endregion
        
        public void SetMeIconActive(bool _isActive) { meIconCard.gameObject.SetActive(_isActive); }

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

