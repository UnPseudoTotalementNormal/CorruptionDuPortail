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

        [Tooltip("Optional. If set, the tooltip anchors to this static rect instead of the object's animated " +
                 "child bounds — keeps it pinned to the resting slot when the visual moves/zooms on hover.")]
        [SerializeField] private RectTransform tooltipBoundsOverride;
        public RectTransform TooltipBoundsOverride => tooltipBoundsOverride;

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