#region

using System;
using DG.Tweening;
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
            baseColor = panelImage.color;
        }
        
        public void OnPointerClick(PointerEventData _eventData)
        {
            transform.DOKill(true);
            transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 1, 0.2f);
            onButtonClicked?.Invoke();
            onButtonClickedUnityEvent?.Invoke();
        }

        public void OnPointerEnter(PointerEventData _eventData)
        {
            panelImage.DOColor(hoverColor, 0.2f);
            onButtonHovered?.Invoke();
        }

        public void OnPointerExit(PointerEventData _eventData)
        {
            panelImage.DOColor(baseColor, 0.2f);
            onButtonUnhovered?.Invoke();
        }

        private void OnDisable()
        {
            if (panelImage != null)
            {
                panelImage.DOColor(disabledColor, 0.2f);
            }
        }
        
        private void OnEnable()
        {
            panelImage.DOColor(baseColor, 0.2f);
        }
    }
}