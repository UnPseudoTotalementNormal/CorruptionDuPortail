#region

using System;
using DG.Tweening;
using Extensions;
using FMODUnity;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#endregion

namespace UI
{
    public class CustomButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private Color baseColor;
        [SerializeField] private Color hoverColor = new Color(0.8f , 0.8f, 0.8f, 1);
        [SerializeField] private Color disabledColor = new Color(0.5f, 0.5f, 0.5f, 1);

        [SerializeField] private Image panelImage;

        [SerializeField] private EventReference clickSound;
        [SerializeField] private EventReference hoverSound;
        [SerializeField] private EventReference unHoverSound;
        
        // False = greyed out (disabledColor) and inert: no click, hover or sound. Runtime only, never serialized.
        public bool Interactable { get; private set; } = true;

        public event Action onButtonClicked;
        public event Action onButtonHovered;
        public event Action onButtonUnhovered;
        [SerializeField] public UnityEvent onButtonClickedUnityEvent = new();

        private void Awake()
        {
            if (panelImage == null)
            {
                panelImage = GetComponentInChildren<Image>();
            }

            if (panelImage != null)
            {
                baseColor = panelImage.color;
            }
        }
        
        public void SetInteractable(bool _interactable)
        {
            if (Interactable == _interactable) return;
            Interactable = _interactable;
            if (panelImage != null) panelImage.DOColor(RestColor, 0.2f).SetLink(gameObject);
        }

        private Color RestColor => Interactable ? baseColor : disabledColor;

        public void OnPointerClick(PointerEventData _eventData)
        {
            if (!Interactable) return;
            transform.DOKill(true);
            transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 1, 0.2f).SetLink(gameObject);
            onButtonClicked?.Invoke();
            onButtonClickedUnityEvent?.Invoke();
            clickSound.TryPlayOneShot();
        }

        public void OnPointerEnter(PointerEventData _eventData)
        {
            if (!Interactable) return;
            if (panelImage != null) panelImage.DOColor(hoverColor, 0.2f).SetLink(gameObject);
            onButtonHovered?.Invoke();
            hoverSound.TryPlayOneShot();
        }

        public void OnPointerExit(PointerEventData _eventData)
        {
            if (!Interactable) return;
            if (panelImage != null) panelImage.DOColor(baseColor, 0.2f).SetLink(gameObject);
            onButtonUnhovered?.Invoke();
            unHoverSound.TryPlayOneShot();
        }

        private void OnDisable()
        {
            if (panelImage != null)
            {
                // Linked: a button disabled on its way to Destroy must not leave a tween on a dead Image.
                panelImage.DOColor(disabledColor, 0.2f).SetLink(gameObject);
            }
        }
        
        private void OnEnable()
        {
            if (panelImage != null) panelImage.DOColor(RestColor, 0.2f).SetLink(gameObject);
        }
    }
}