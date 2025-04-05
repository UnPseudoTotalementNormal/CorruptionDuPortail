using System;
using DG.Tweening;
using Network;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI
{
    public class CustomButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private Color baseColor;
        [SerializeField] private Color hoverColor;

        private Image panelImage;
        
        public event Action onButtonClicked;
        [SerializeField] public UnityEvent onButtonClickedUnityEvent = new();

        private void Awake()
        {
            panelImage = GetComponent<Image>();
            baseColor = panelImage.color;
        }
        
        public void OnPointerClick(PointerEventData eventData)
        {
            transform.DOKill(true);
            transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 1, 0.2f);
            onButtonClicked?.Invoke();
            onButtonClickedUnityEvent?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            panelImage.DOColor(hoverColor, 0.2f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            panelImage.DOColor(baseColor, 0.2f);
        }
    }
}