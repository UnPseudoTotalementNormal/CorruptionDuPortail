using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Extensions;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace TooltipSystem
{
    public class TooltipManager : MonoBehaviour
    {
        public static TooltipManager instance;
        
        [field: FormerlySerializedAs("<tooltipLinkReferenceHolder>k__BackingField")] [field:SerializeField] public TooltipLinkParser tooltipLinkParser { get; private set; }
        
        [SerializeField] private TooltipWindow tooltipPrefab;
        
        [SerializeField] private Transform tooltipCanvas;
        
        private List<TooltipInstanceInfo> tooltipInstances = new();

        // Story 11.4 (Epic 11 / D5): the link-colouring policy extracted to a pure EditMode-tested Domain POCO.
        private const string LINK_COLOR_HEX = "6fb5d1";
        private readonly CorruptionDuPortail.Domain.TooltipLinkFormatter _linkFormatter = new();

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void Update()
        {
            foreach (var _tooltipInstanceInfo in tooltipInstances.ToList())
            {
                if (_tooltipInstanceInfo.linkedGameObject == null)
                {
                    CloseTooltip(_tooltipInstanceInfo.tooltipWindow, _tooltipInstanceInfo);
                    return;
                }
                
                PlaceTooltip(_tooltipInstanceInfo.linkedTooltipTrigger, _tooltipInstanceInfo.linkedGameObject, _tooltipInstanceInfo.tooltipWindow);
                
                if (_tooltipInstanceInfo.isMouseOverLinkedGameObject || _tooltipInstanceInfo.isMouseOverTooltipWindow 
                                                                     || IsTooltipOpenForGameObject(_tooltipInstanceInfo.tooltipWindow.gameObject))
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
            _newTooltip.DescriptionText.text = _linkFormatter.WrapLinksWithColor(_tooltipDescription, LINK_COLOR_HEX);
            _newTooltip.tooltipOffsetDirection = _tooltipTrigger.tooltipOffsetDirection;
            
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
            _tooltipInstanceInfo.tooltipWindow.enabled = false;

            _tooltip.transform.DOScale(Vector3.zero, 0.35f).SetEase(Ease.OutQuint).onComplete = () =>
            {
                Destroy(_tooltip.gameObject);
            };
        }
        
        public bool IsTooltipOpenForGameObject(GameObject _linkedGameObject)
        {
            return tooltipInstances.Any(x => x.linkedGameObject == _linkedGameObject);
        }
        
        public TooltipInstanceInfo GetTooltipInstanceInfo(GameObject gameObject)
        {
            return tooltipInstances.FirstOrDefault(x => x.linkedGameObject == gameObject);
        }
        
        private void PlaceTooltip(ITooltipTrigger _tooltipTrigger, GameObject _linkedGameObject, TooltipWindow _newTooltip)
        {
            Canvas.ForceUpdateCanvases();
            RectTransform _tooltipRect = _newTooltip.GetComponent<RectTransform>();
            var (_tooltipBoundingBoxSize, _tooltipScreenPos) = 
                GetScreenBoundingBoxAndCenter(_tooltipRect.GetComponentsInChildren<RectTransform>());
            
            // Check if the linked game object is a UI element (has RectTransform)
            RectTransform _linkedRectTransform = _linkedGameObject.GetComponent<RectTransform>();
            Vector2 _componentBoundingBoxSize;
            Vector2 _componentScreenPos;

            RectTransform _boundsOverride = _tooltipTrigger.TooltipBoundsOverride;

            if (_boundsOverride != null)
            {
                // Anchor to a fixed rect (e.g. a resting slot) so animated visuals don't drag the tooltip.
                (_componentBoundingBoxSize, _componentScreenPos) =
                    GetScreenBoundingBoxAndCenter(new[] { _boundsOverride }, Camera.main);
            }
            else if (_linkedRectTransform != null)
            {
                (_componentBoundingBoxSize, _componentScreenPos) =
                    GetScreenBoundingBoxAndCenter(_linkedGameObject.GetComponentsInChildren<RectTransform>(), Camera.main);
            }
            else
            {
                // 3D Object - use GetWorldBounds extension
                Bounds _worldBounds = _linkedGameObject.transform.GetWorldBounds();
                _componentScreenPos = RectTransformUtility.WorldToScreenPoint(Camera.main, _worldBounds.center);
                
                // Calculate screen-space size from world bounds
                Vector3 _boundsMin = _worldBounds.min;
                Vector3 _boundsMax = _worldBounds.max;
                Vector2 _screenMin = RectTransformUtility.WorldToScreenPoint(Camera.main, _boundsMin);
                Vector2 _screenMax = RectTransformUtility.WorldToScreenPoint(Camera.main, _boundsMax);
                _componentBoundingBoxSize = new Vector2(
                    Mathf.Abs(_screenMax.x - _screenMin.x), 
                    Mathf.Abs(_screenMax.y - _screenMin.y)
                );
            }
            
            _tooltipRect.position = _componentScreenPos + _tooltipTrigger.tooltipOffsetDirection * (_componentBoundingBoxSize / 2f + _tooltipBoundingBoxSize / 2f);
        }

        private (Vector2 screenBoundingBoxSize, Vector2 screenPos) GetScreenBoundingBoxAndCenter(RectTransform[] _targetRects, Camera _camera = null)
        {
            if (_targetRects.Length == 0)
            {
                return (Vector2.zero, Vector2.zero);
            }

            Canvas parentCanvas = _targetRects[0].GetComponentInParent<Canvas>();
            if (!parentCanvas)
            {
                return (Vector2.zero, Vector2.zero);
            }

            Vector3 _min = Vector3.positiveInfinity;
            Vector3 _max = Vector3.negativeInfinity;
            Vector3[] _corners = new Vector3[4];

            // WorldSpace: use WorldToScreenPoint
            if (parentCanvas.renderMode == RenderMode.WorldSpace)
            {
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
            // ScreenSpace Overlay or Camera: use local positions in the Canvas
            else
            {
                foreach (var _rect in _targetRects)
                {
                    _rect.GetWorldCorners(_corners);
                    for (int i = 0; i < 4; i++)
                    {
                        // Convert corners to Canvas coordinates
                        Vector2 canvasPos = Vector2.zero;
                        if (parentCanvas.renderMode == RenderMode.ScreenSpaceCamera && parentCanvas.worldCamera != null)
                        {
                            canvasPos = RectTransformUtility.WorldToScreenPoint(parentCanvas.worldCamera, _corners[i]);
                        }
                        else // ScreenSpaceOverlay
                        {
                            canvasPos = RectTransformUtility.WorldToScreenPoint(null, _corners[i]);
                        }
                        _min = Vector3.Min(_min, canvasPos);
                        _max = Vector3.Max(_max, canvasPos);
                    }
                }
                Vector3 _boundingBoxCenter = (_min + _max) * 0.5f;
                Vector2 _screenBoundingBoxSize = new Vector2(Mathf.Abs(_max.x - _min.x), Mathf.Abs(_max.y - _min.y));
                Vector2 _screenPos = new Vector2(_boundingBoxCenter.x, _boundingBoxCenter.y);
                return (_screenBoundingBoxSize, _screenPos);
            }
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
