using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TooltipSystem
{
    public class HoverTooltipComponent : MonoBehaviour, ITooltipTrigger, IPointerEnterHandler, IPointerExitHandler
    {
        public event Action onTooltipTryClose;
        public event Action onTooltipForceClose;

        [SerializeField] private string tooltipTitle;
        [SerializeField] private string tooltipDescription;
        
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
            TooltipWindow _newTooltip = TooltipManager.instance.CreateNewTooltip(gameObject, tooltipTitle, tooltipDescription);
            PlaceTooltip(_newTooltip);
        }

        private void PlaceTooltip(TooltipWindow _newTooltip)
        {
            RectTransform tooltipRect = _newTooltip.GetComponent<RectTransform>();
            RectTransform[] targetRects = GetComponentsInChildren<RectTransform>();

            if (targetRects.Length == 0)
                return;

            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            Vector3[] corners = new Vector3[4];

            foreach (var rect in targetRects)
            {
                rect.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    min = Vector3.Min(min, corner);
                    max = Vector3.Max(max, corner);
                }
            }

            Vector3 boundingBoxSize = new Vector3(
                Mathf.Abs(max.x - min.x),
                Mathf.Abs(max.y - min.y),
                Mathf.Abs(max.z - min.z)
            );
            Vector3 boundingBoxCenter = (min + max) * 0.5f;
            
            Vector2 screenMin = RectTransformUtility.WorldToScreenPoint(Camera.main, min);
            Vector2 screenMax = RectTransformUtility.WorldToScreenPoint(Camera.main, max);
            Vector2 screenBoundingBoxSize = screenMax - screenMin;
            
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(Camera.main, boundingBoxCenter);
            tooltipRect.position = screenPos + Vector2.up * screenBoundingBoxSize.y / 2f; // + la moitié du tooltip aussi
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