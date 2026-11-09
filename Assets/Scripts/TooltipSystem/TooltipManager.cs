using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using Diagnostics.Logs;
using Extensions;
using UnityEngine;
using UnityEngine.Serialization;

namespace TooltipSystem
{
    /// <summary>
    /// The hover bubble (mockups infobulles v1: A1 parchment, T1 timing, K2 keyword cards, P0 beside the trigger).
    /// One bubble and a few keyword cards, made once and reused. Triggers report the pointer
    /// (<see cref="HoverTooltipComponent"/>); <see cref="TooltipHoverTimeline"/> decides when it opens and closes; each
    /// link of the description gets its card beside the bubble, opened with it.
    /// </summary>
    public class TooltipManager : MonoBehaviour
    {
        public static TooltipManager instance;

        [field: FormerlySerializedAs("<tooltipLinkReferenceHolder>k__BackingField")] [field:SerializeField] public TooltipLinkParser tooltipLinkParser { get; private set; }

        [SerializeField] private TooltipWindow tooltipPrefab;

        [SerializeField] private Transform tooltipCanvas;

        private const string LINK_COLOR_HEX = "7a1f12";
        private const float FADE_IN = 0.12f;
        private const float FADE_OUT = 0.1f;
        private const float SLIDE = 6f;
        private const float KEYWORD_GAP = 12f;

        private readonly TooltipLinkFormatter _linkFormatter = new();
        private readonly TooltipHoverTimeline _timeline = new();
        private readonly List<TooltipWindow> _keywordCards = new();
        private readonly Vector3[] _corners = new Vector3[4];
        private Vector2[] _cardSizes = new Vector2[0];

        private Canvas _canvas;
        private TooltipWindow _bubble;
        private int _keywordCount;

        private HoverTooltipComponent _hovered;
        private HoverTooltipComponent _shown;
        private HoverTooltipComponent _dismissed;
        private RectTransform[] _anchorRects;
        private Renderer[] _anchorRenderers;
        private bool _keptOnScreenLogged;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            _canvas = tooltipCanvas != null ? tooltipCanvas.GetComponentInParent<Canvas>() : null;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        internal void PointerEntered(HoverTooltipComponent _trigger)
        {
            _hovered = _trigger;
        }

        internal void PointerExited(HoverTooltipComponent _trigger)
        {
            if (_hovered == _trigger) _hovered = null;
            if (_dismissed == _trigger) _dismissed = null;
        }

        internal void TriggerGone(HoverTooltipComponent _trigger)
        {
            PointerExited(_trigger);
            if (_shown == _trigger) Hide("trigger.gone");
        }

        internal void ContentChanged(HoverTooltipComponent _trigger)
        {
            if (_shown == _trigger && _trigger.HasContent) Fill(_trigger);
        }

        public bool IsTooltipOpenForGameObject(GameObject _linkedGameObject)
        {
            return _shown != null && _shown.gameObject == _linkedGameObject;
        }

        private void Update()
        {
            if (!ReferenceEquals(_shown, null) && _shown == null)
            {
                Hide("trigger.destroyed");
            }

            // A click on what the bubble describes (using a power opens its target picker) puts the bubble away until
            // the pointer leaves that trigger.
            var _pointer = UnityEngine.InputSystem.Pointer.current;
            if (_shown != null && _pointer != null && _pointer.press.wasPressedThisFrame)
            {
                _dismissed = _shown;
                Hide("click");
            }

            HoverTooltipComponent _candidate = _hovered != null && _hovered.isActiveAndEnabled && _hovered != _dismissed
                                               && _hovered.HasContent ? _hovered : null;
            switch (_timeline.Tick(_candidate != null ? _candidate.GetHashCode() : 0, Time.unscaledTime))
            {
                case TooltipAction.Show:
                    Show(_candidate);
                    break;
                case TooltipAction.Hide:
                    HideVisuals("left");
                    break;
            }
        }

        private void LateUpdate()
        {
            if (_shown != null)
            {
                Place();
            }
        }

        private void Show(HoverTooltipComponent _trigger)
        {
            if (_shown != null && _shown != _trigger)
            {
                GameLog.Info("ui", "ui.tooltip.close", "target", _shown.name, "reason", "switch");
            }
            _shown = _trigger;
            _anchorRects = _trigger.TooltipBoundsOverride == null && _trigger.GetComponent<RectTransform>() != null
                ? _trigger.GetComponentsInChildren<RectTransform>() : null;
            _anchorRenderers = _trigger.TooltipBoundsOverride == null && _anchorRects == null
                ? _trigger.GetComponentsInChildren<Renderer>() : null;
            _keptOnScreenLogged = false;

            Fill(_trigger);
            Place();
            _bubble.FadeIn(FADE_IN, SLIDE);
            for (int _i = 0; _i < _keywordCount; _i++) _keywordCards[_i].FadeIn(FADE_IN, SLIDE);

            GameLog.Info("ui", "ui.tooltip.open", "target", _trigger.name, "title", _trigger.Title, "keywords", _keywordCount);
        }

        private void Fill(HoverTooltipComponent _trigger)
        {
            string _description = _trigger.Description;
            Bubble().SetContent(_trigger.Title,
                string.IsNullOrEmpty(_description) ? null : _linkFormatter.WrapLinksWithColor(_description, LINK_COLOR_HEX), false);

            int _previous = _keywordCount;
            _keywordCount = 0;
            foreach (string _id in _linkFormatter.ExtractLinkIds(_description))
            {
                TooltipReference _reference = tooltipLinkParser != null ? tooltipLinkParser.GetTooltipReference(_id) : null;
                if (_reference == null)
                {
                    GameLog.Warn("ui", "ui.tooltip.keyword.missing", "target", _trigger.name, "link", _id);
                    continue;
                }
                TooltipWindow _card = KeywordCard(_keywordCount++);
                _card.SetContent(_reference.title,
                    string.IsNullOrEmpty(_reference.description) ? null : _linkFormatter.WrapLinksWithColor(_reference.description, LINK_COLOR_HEX), true);
                if (_bubble.gameObject.activeSelf && _keywordCount > _previous) _card.FadeIn(FADE_IN, 0f);
            }
            for (int _i = _keywordCount; _i < _previous; _i++) if (_keywordCards[_i] != null) _keywordCards[_i].FadeOut(FADE_OUT);
        }

        private void Hide(string _reason)
        {
            if (_timeline.Hide(Time.unscaledTime) == TooltipAction.Hide)
            {
                HideVisuals(_reason);
            }
        }

        private void HideVisuals(string _reason)
        {
            if (!ReferenceEquals(_shown, null))
            {
                GameLog.Info("ui", "ui.tooltip.close", "target", _shown != null ? _shown.name : "destroyed", "reason", _reason);
            }
            _shown = null;
            _anchorRects = null;
            _anchorRenderers = null;
            if (_bubble != null) _bubble.FadeOut(FADE_OUT);
            for (int _i = 0; _i < _keywordCount; _i++) if (_keywordCards[_i] != null) _keywordCards[_i].FadeOut(FADE_OUT);
            _keywordCount = 0;
        }

        private TooltipWindow Bubble()
        {
            if (_bubble == null)
            {
                _bubble = Instantiate(tooltipPrefab, tooltipCanvas);
                _bubble.name = "TooltipBubble";
                _bubble.gameObject.SetActive(false);
            }
            return _bubble;
        }

        private TooltipWindow KeywordCard(int _index)
        {
            while (_keywordCards.Count <= _index)
            {
                TooltipWindow _card = Instantiate(tooltipPrefab, tooltipCanvas);
                _card.name = "TooltipKeyword" + _keywordCards.Count;
                _card.gameObject.SetActive(false);
                _keywordCards.Add(_card);
            }
            return _keywordCards[_index];
        }

        private void Place()
        {
            float _scale = _canvas != null ? _canvas.scaleFactor : 1f;
            Vector2 _screen = new Vector2(Screen.width, Screen.height);
            (Vector2 _triggerSize, Vector2 _triggerCentre) = TriggerScreenBox();
            Vector2 _bubbleSize = _bubble.Rect.rect.size * _scale;
            Vector2 _direction = _shown.tooltipOffsetDirection.sqrMagnitude > 0f ? _shown.tooltipOffsetDirection.normalized : Vector2.up;

            (Vector2 _placed, bool _flipped) = KeepOnScreen(_triggerCentre, _triggerSize, _bubbleSize, _direction,
                _shown.tooltipPixelOffset, _screen);
            if (_flipped && !_keptOnScreenLogged)
            {
                _keptOnScreenLogged = true;
                GameLog.Info("ui", "ui.tooltip.kept.on.screen", "target", _shown.name, "to", _placed);
            }
            _bubble.Rect.position = _placed + Vector2.down * (_bubble.Slide * _scale);

            if (_keywordCount == 0)
            {
                return;
            }
            if (_cardSizes.Length < _keywordCount) _cardSizes = new Vector2[_keywordCount];
            for (int _i = 0; _i < _keywordCount; _i++) _cardSizes[_i] = _keywordCards[_i].Rect.rect.size * _scale;
            Vector2[] _centres = KeywordCardCentres(_placed, _bubbleSize, _cardSizes, _keywordCount, KEYWORD_GAP * _scale, _screen);
            for (int _i = 0; _i < _keywordCount; _i++)
            {
                _keywordCards[_i].Rect.position = _centres[_i] + Vector2.down * (_keywordCards[_i].Slide * _scale);
            }
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

        /// <summary>
        /// Centres (screen pixels, y up) of the keyword cards beside a bubble: right of it, tops aligned with the
        /// bubble's top and stacked down; on its left when the right side of the screen has no room; each kept inside
        /// the screen vertically.
        /// </summary>
        public static Vector2[] KeywordCardCentres(Vector2 _bubbleCentre, Vector2 _bubbleSize, Vector2[] _cardSizes,
            int _count, float _gap, Vector2 _screen)
        {
            var _centres = new Vector2[_count];
            float _widest = 0f;
            for (int _i = 0; _i < _count; _i++) _widest = Mathf.Max(_widest, _cardSizes[_i].x);
            bool _right = _bubbleCentre.x + _bubbleSize.x / 2f + _gap + _widest <= _screen.x;

            float _top = _bubbleCentre.y + _bubbleSize.y / 2f;
            for (int _i = 0; _i < _count; _i++)
            {
                Vector2 _size = _cardSizes[_i];
                float _x = _right
                    ? _bubbleCentre.x + _bubbleSize.x / 2f + _gap + _size.x / 2f
                    : _bubbleCentre.x - _bubbleSize.x / 2f - _gap - _size.x / 2f;
                float _y = Mathf.Clamp(_top - _size.y / 2f, _size.y / 2f, Mathf.Max(_size.y / 2f, _screen.y - _size.y / 2f));
                _centres[_i] = new Vector2(_x, _y);
                _top -= _size.y + _gap;
            }
            return _centres;
        }

        private (Vector2 size, Vector2 centre) TriggerScreenBox()
        {
            Camera _camera = Camera.main;
            if (_shown.TooltipBoundsOverride != null)
            {
                // Anchor to a fixed rect (e.g. a resting slot) so animated visuals don't drag the tooltip.
                return RectsScreenBox(_shown.TooltipBoundsOverride, null, _camera);
            }
            if (_anchorRects != null)
            {
                return RectsScreenBox(null, _anchorRects, _camera);
            }

            // 3D object: project all 8 world-AABB corners so the screen box matches the on-screen silhouette.
            Bounds _worldBounds = new Bounds(_shown.transform.position, Vector3.zero);
            bool _first = true;
            foreach (Renderer _renderer in _anchorRenderers)
            {
                if (_renderer == null) continue;
                if (_first) { _worldBounds = _renderer.bounds; _first = false; }
                else _worldBounds.Encapsulate(_renderer.bounds);
            }
            if (!_worldBounds.TryGetScreenBounds(_camera, out Vector2 _size, out Vector2 _centre))
            {
                // Fully behind the camera (or no camera) - fall back to the bounds-centre projection.
                return (Vector2.zero, RectTransformUtility.WorldToScreenPoint(_camera, _worldBounds.center));
            }
            return (_size, _centre);
        }

        private (Vector2 size, Vector2 centre) RectsScreenBox(RectTransform _single, RectTransform[] _rects, Camera _camera)
        {
            RectTransform _firstRect = _single != null ? _single : _rects.Length > 0 ? _rects[0] : null;
            Canvas _parentCanvas = _firstRect != null ? _firstRect.GetComponentInParent<Canvas>() : null;
            if (_parentCanvas == null)
            {
                return (Vector2.zero, Vector2.zero);
            }
            Camera _projection = _parentCanvas.renderMode == RenderMode.WorldSpace ? _camera
                : _parentCanvas.renderMode == RenderMode.ScreenSpaceCamera ? _parentCanvas.worldCamera : null;

            Vector2 _min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 _max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            int _count = _single != null ? 1 : _rects.Length;
            for (int _r = 0; _r < _count; _r++)
            {
                RectTransform _rect = _single != null ? _single : _rects[_r];
                if (_rect == null) continue;
                _rect.GetWorldCorners(_corners);
                for (int _c = 0; _c < 4; _c++)
                {
                    Vector2 _point = RectTransformUtility.WorldToScreenPoint(_projection, _corners[_c]);
                    _min = Vector2.Min(_min, _point);
                    _max = Vector2.Max(_max, _point);
                }
            }
            return float.IsInfinity(_min.x) ? (Vector2.zero, Vector2.zero) : (_max - _min, (_min + _max) / 2f);
        }
    }
}
