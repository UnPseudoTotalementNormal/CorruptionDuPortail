using System;
using System.Collections;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TooltipSystem
{
    public class HoverTooltipComponent : MonoBehaviour, ITooltipTrigger, IPointerEnterHandler, IPointerExitHandler
    {
        public event Action onMouseEnterTrigger;
        public event Action onMouseExitTrigger;
        public event Action onTooltipForceClose;

        [SerializeField] private string tooltipTitle;
        [SerializeField] private string tooltipDescription;
        [field:SerializeField] public Vector2 tooltipOffsetDirection { get; set; } = Vector2.up;
        
        private void Start()
        {
            tooltipOffsetDirection.Normalize();
        }

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
            onMouseEnterTrigger?.Invoke();

            if (TooltipManager.instance.IsTooltipOpenForGameObject(gameObject))
            {
                return;
            }
            
            TooltipWindow _newTooltip = TooltipManager.instance.CreateNewTooltipFromGameObject(gameObject, tooltipTitle, tooltipDescription);
        }
        
        public void OnPointerExit(PointerEventData _eventData)
        {
            onMouseExitTrigger?.Invoke();
        }

        private void OnDisable()
        {
            onTooltipForceClose?.Invoke();
        }
        
        
    }
}