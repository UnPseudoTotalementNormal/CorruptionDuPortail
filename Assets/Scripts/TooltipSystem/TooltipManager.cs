using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace TooltipSystem
{
    public class TooltipManager : MonoBehaviour
    {
        public static TooltipManager instance;
        
        [SerializeField] private TooltipWindow tooltipPrefab;
        
        [SerializeField] private Transform tooltipCanvas;
        
        private List<TooltipInstanceInfo> tooltipInstances = new();

        private void Awake()
        {
            instance = this;
        }

        private void Update()
        {
            foreach (var _tooltipInstanceInfo in tooltipInstances.ToList())
            {
                PlaceTooltip(_tooltipInstanceInfo.linkedTooltipTrigger, _tooltipInstanceInfo.linkedGameObject, _tooltipInstanceInfo.tooltipWindow);
                
                if (_tooltipInstanceInfo.isMouseOverLinkedGameObject || _tooltipInstanceInfo.isMouseOverTooltipWindow)
                {
                    _tooltipInstanceInfo.tooltipNoHoverTimer = _tooltipInstanceInfo.tooltipNoHoverTime;
                    continue;
                }

                _tooltipInstanceInfo.tooltipNoHoverTimer -= Time.deltaTime;

                if (_tooltipInstanceInfo.tooltipNoHoverTimer <= 0)
                {
                    CloseTooltip(_tooltipInstanceInfo.tooltipWindow, _tooltipInstanceInfo);
                }
            }
        }

        public TooltipWindow CreateNewTooltipFromGameObject(GameObject _linkedGameObject, string _tooltipTitle, string _tooltipDescription)
        {
            ITooltipTrigger _tooltipTrigger = _linkedGameObject.GetComponent<ITooltipTrigger>();
            
            Assert.IsNotNull(_tooltipTrigger, $"GameObject {_linkedGameObject.name} does not have a component that implements ITooltipTrigger. It is required to use CreateNewTooltipFromGameObject."); 
            TooltipWindow _newTooltip = Instantiate(tooltipPrefab, tooltipCanvas);
            
            _newTooltip.TitleText.text = _tooltipTitle;
            _newTooltip.DescriptionText.text = _tooltipDescription;
            
            LayoutRebuilder.ForceRebuildLayoutImmediate(_newTooltip.GetComponent<RectTransform>());
            
            var _tooltipInstanceInfo = new TooltipInstanceInfo(_linkedGameObject, _newTooltip);
            tooltipInstances.Add(_tooltipInstanceInfo);

            _tooltipTrigger.onMouseEnterTrigger += () => { OnMouseEnterComponent(_tooltipInstanceInfo); };
            _tooltipTrigger.onMouseExitTrigger += () => { OnMouseExitComponent(_tooltipInstanceInfo); };
            _tooltipTrigger.onTooltipForceClose += () => { CloseTooltip(_newTooltip, _tooltipInstanceInfo); };
            
            _newTooltip.onMouseEnterTrigger += () => { _tooltipInstanceInfo.isMouseOverTooltipWindow = true; };
            _newTooltip.onMouseExitTrigger += () => { _tooltipInstanceInfo.isMouseOverTooltipWindow = false; };
            
            _newTooltip.transform.localScale = Vector3.zero;
            _newTooltip.transform.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutQuint);
            
            PlaceTooltip(_tooltipTrigger, _linkedGameObject, _newTooltip);
            
            return _newTooltip;
        }

        private void OnMouseEnterComponent(TooltipInstanceInfo _tooltipInstanceInfo)
        {
            if (!_tooltipInstanceInfo.tooltipWindow)
            {
                return;
            }
            
            _tooltipInstanceInfo.isMouseOverLinkedGameObject = true;
        }

        private void OnMouseExitComponent(TooltipInstanceInfo _tooltipInstanceInfo)
        {
            if (!_tooltipInstanceInfo.tooltipWindow)
            {
                return;
            }
            
            _tooltipInstanceInfo.isMouseOverLinkedGameObject = false;
        }
        
        public void CloseTooltip(TooltipWindow _tooltip, TooltipInstanceInfo _tooltipInstanceInfo = null)
        {
            if (!_tooltip)
            {
                return;
            }

            tooltipInstances.Remove(_tooltipInstanceInfo ?? tooltipInstances.Find(x => x.tooltipWindow == _tooltip));

            _tooltip.transform.DOScale(Vector3.zero, 0.35f).SetEase(Ease.OutQuint).onComplete = () =>
            {
                Destroy(_tooltip.gameObject);
            };
        }
        
        public bool IsTooltipOpenForGameObject(GameObject _linkedGameObject)
        {
            return tooltipInstances.Any(x => x.linkedGameObject == _linkedGameObject);
        }
        
        private void PlaceTooltip(ITooltipTrigger _tooltipTrigger, GameObject _linkedGameObject, TooltipWindow _newTooltip)
        {
            Canvas.ForceUpdateCanvases();
            RectTransform _tooltipRect = _newTooltip.GetComponent<RectTransform>();
            var (_tooltipBoundingBoxSize, _tooltipScreenPos) = 
                GetScreenBoundingBoxAndCenter(_tooltipRect.GetComponentsInChildren<RectTransform>());
            var (_componentBoundingBoxSize, _componentScreenPos) = 
                GetScreenBoundingBoxAndCenter(_linkedGameObject.GetComponentsInChildren<RectTransform>(), Camera.main);
            _tooltipRect.position = _componentScreenPos + _tooltipTrigger.tooltipOffsetDirection * (_componentBoundingBoxSize / 2f + _tooltipBoundingBoxSize / 2f);
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

    public class TooltipInstanceInfo
    {
        public GameObject linkedGameObject;
        public ITooltipTrigger linkedTooltipTrigger;
        public TooltipWindow tooltipWindow;
        public bool isMouseOverLinkedGameObject = true;
        public bool isMouseOverTooltipWindow = false;

        public float tooltipNoHoverTimer;
        public float tooltipNoHoverTime = 0; // Time before tooltip closes when not hovered
        
        public TooltipInstanceInfo(GameObject _linkedGameObject, TooltipWindow _tooltipWindow)
        {
            linkedGameObject = _linkedGameObject;
            linkedTooltipTrigger = _linkedGameObject.GetComponent<ITooltipTrigger>();
            tooltipWindow = _tooltipWindow;
            tooltipNoHoverTimer = tooltipNoHoverTime;
        }
    }
}
