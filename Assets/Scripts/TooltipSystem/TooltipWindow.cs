using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TooltipSystem
{
    public class TooltipWindow : MonoBehaviour, ITooltipTrigger, IPointerEnterHandler, IPointerExitHandler
    {
        public TMP_Text TitleText;
        public TMP_Text DescriptionText;
        
        public event Action onMouseEnterTrigger;
        public event Action onMouseExitTrigger;
        public event Action onTooltipForceClose;


        public void OnPointerEnter(PointerEventData eventData)
        {
            onMouseEnterTrigger?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            onMouseExitTrigger?.Invoke();
        }
    }
}