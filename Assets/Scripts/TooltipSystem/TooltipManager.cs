using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Diagnostics.Logs;
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
                // 3D Object - project all 8 world-AABB corners so the screen box matches the on-screen
                // silhouette. Projecting only min/max is wrong under perspective (diagonal corners differ
                // in depth and don't bound the projection).
                Bounds _worldBounds = _linkedGameObject.transform.GetWorldBounds();
                if (!_worldBounds.TryGetScreenBounds(Camera.main, out _componentBoundingBoxSize, out _componentScreenPos))
                {
                    // Fully behind the camera (or no camera) - fall back to the bounds-center projection.
                    _componentScreenPos = RectTransformUtility.WorldToScreenPoint(Camera.main, _worldBounds.center);
                    _componentBoundingBoxSize = Vector2.zero;
                }
            }
            
            Vector2 _wanted = _componentScreenPos
                + _tooltipTrigger.tooltipOffsetDirection * (_componentBoundingBoxSize / 2f + _tooltipBoundingBoxSize / 2f)
                + _tooltipTrigger.tooltipPixelOffset;
            (Vector2 _placed, bool _flipped) = KeepOnScreen(_componentScreenPos, _componentBoundingBoxSize,
                _tooltipBoundingBoxSize, _tooltipTrigger.tooltipOffsetDirection, _tooltipTrigger.tooltipPixelOffset,
                new Vector2(Screen.width, Screen.height));
            if (_placed != _wanted)
            {
                GameLog.Info("ui", "ui.tooltip.kept.on.screen", "target", _linkedGameObject.name, "flipped", _flipped,
                    "from", _wanted, "to", _placed);
            }

            _tooltipRect.position = _placed;
        }

        /// <summary>
        /// Centre of a tooltip placed beside its trigger, kept inside the screen. A trigger near a screen edge (the
        /// robot counter in the top-right corner, playtest 2026-10-29) pushed its tooltip out of the screen: the
        /// tooltip flips to the other side on each axis where it does not fit, then the whole box is clamped inside.
        /// </summary>
        public static (Vector2 centre, bool flipped) KeepOnScreen(Vector2 _triggerCentre, Vector2 _triggerSize,
            Vector2 _tooltipSize, Vector2 _direction, Vector2 _pixelOffset, Vector2 _screen)
        {
            Vector2 _gap = _triggerSize / 2f + _tooltipSize / 2f;
            Vector2 _half = _tooltipSize / 2f;
            Vector2 _centre = _triggerCentre + _direction * _gap + _pixelOffset;

            bool _flipped = false;
            for (int _axis = 0; _axis < 2; _axis++)
            {
                bool _out = _centre[_axis] - _half[_axis] < 0f || _centre[_axis] + _half[_axis] > _screen[_axis];
                if (_out && !Mathf.Approximately(_direction[_axis], 0f))
                {
                    _direction[_axis] = -_direction[_axis];
                    _flipped = true;
                }
            }
            if (_flipped)
            {
                _centre = _triggerCentre + _direction * _gap + _pixelOffset;
            }
            return (new Vector2(
                Mathf.Clamp(_centre.x, _half.x, Mathf.Max(_half.x, _screen.x - _half.x)),
                Mathf.Clamp(_centre.y, _half.y, Mathf.Max(_half.y, _screen.y - _half.y))), _flipped);
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
