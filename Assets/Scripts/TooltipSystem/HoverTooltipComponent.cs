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

        private Canvas canvas;
        
        private void Start()
        {
            canvas = GetComponentInParent<Canvas>();
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
            Canvas.ForceUpdateCanvases();
            PlaceTooltip(_newTooltip);
        }
        
        public void OnPointerExit(PointerEventData _eventData)
        {
            onMouseExitTrigger?.Invoke();
        }

        private void OnDisable()
        {
            onTooltipForceClose?.Invoke();
        }
        
        private void PlaceTooltip(TooltipWindow _newTooltip)
        {
            RectTransform _tooltipRect = _newTooltip.GetComponent<RectTransform>();
            var (_tooltipBoundingBoxSize, _tooltipScreenPos) = GetScreenBoundingBoxAndCenter(_tooltipRect.GetComponentsInChildren<RectTransform>());
            var (_componentBoundingBoxSize, _componentScreenPos) = GetScreenBoundingBoxAndCenter(GetComponentsInChildren<RectTransform>(), Camera.main);
            _tooltipRect.position = _componentScreenPos + Vector2.up * (_componentBoundingBoxSize.y / 2f + _tooltipBoundingBoxSize.y / 2f);
        }

        private (Vector2 screenBoundingBoxSize, Vector2 screenPos) GetScreenBoundingBoxAndCenter(RectTransform[] _targetRects, Camera _camera = null)
        {
            if (_targetRects.Length == 0)
                return (Vector2.zero, Vector2.zero);

            Vector3 _min = Vector3.positiveInfinity;
            Vector3 _max = Vector3.negativeInfinity;
            Vector3[] _corners = new Vector3[4];

            foreach (var _rect in _targetRects)
            {
                _rect.GetWorldCorners(_corners);
                foreach (var _corner in _corners)
                {
                    _min = Vector3.Min(_min, _corner);
                    _max = Vector3.Max(_max, _corner);
                }
            }

            Vector3 _boundingBoxCenter = (_min + _max) * 0.5f;
            Vector2 _screenMin = RectTransformUtility.WorldToScreenPoint(_camera, _min);
            Vector2 _screenMax = RectTransformUtility.WorldToScreenPoint(_camera, _max);
            Vector2 _screenBoundingBoxSize = new Vector2(Mathf.Abs(_screenMax.x - _screenMin.x), Mathf.Abs(_screenMax.y - _screenMin.y));
            Vector2 _screenPos = RectTransformUtility.WorldToScreenPoint(_camera, _boundingBoxCenter);
            return (_screenBoundingBoxSize, _screenPos);
        }
    }
}