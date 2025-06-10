using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TooltipSystem
{
    public class HoverTooltipComponent : MonoBehaviour, ITooltipTrigger, IPointerEnterHandler, IPointerExitHandler
    {
        public event Action onTooltipTryClose;
        public event Action onTooltipForceClose;

        private string tooltipTitle;
        private string tooltipDescription;
        
        public void SetTooltipTitle(string _title)
        {
            tooltipTitle = _title;
        }
        
        public void SetTooltipDescription(string _description)
        {
            tooltipDescription = _description;
        }

        public void OnPointerEnter(PointerEventData _eventData)
        {
            TooltipManager.instance.CreateNewTooltip(gameObject, tooltipTitle, tooltipDescription);
        }

        public void OnPointerExit(PointerEventData _eventData)
        {
            onTooltipTryClose?.Invoke();
        }

        private void OnDisable()
        {
            onTooltipForceClose?.Invoke();
        }
    }
}