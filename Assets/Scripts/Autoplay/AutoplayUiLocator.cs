#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Autoplay
{
    /// <summary>
    /// Real-input mode: where a target sits on screen and what the real EventSystem would hit there.
    /// A target is clickable when the first raycast hit resolves (through the same handler lookup the EventSystem uses)
    /// to the target itself or one of its children. Positions are screen pixels, origin bottom-left.
    /// </summary>
    public static class AutoplayUiLocator
    {
        public readonly struct Probe
        {
            public readonly bool found;
            public readonly Vector2 screen;
            public readonly GameObject hit;
            public readonly string reason;

            public Probe(bool _found, Vector2 _screen, GameObject _hit, string _reason)
            {
                found = _found;
                screen = _screen;
                hit = _hit;
                reason = _reason;
            }

            public string Describe(GameObject _target) => string.Format(CultureInfo.InvariantCulture,
                "target={0} pos={1:0},{2:0} hit={3}{4}{5}", PathOf(_target), screen.x, screen.y, PathOf(hit),
                IsUitkHit(hit) ? " uitk=" + DescribeUitkPick(screen) : "",
                string.IsNullOrEmpty(reason) ? "" : " reason=" + reason);
        }

        private static readonly List<RaycastResult> Hits = new();

        /// <summary>Screen point of <paramref name="_target"/>: rect centre for uGUI, bounds centre for a collider.</summary>
        public static bool TryScreenPoint(GameObject _target, out Vector2 _screen, out string _reason)
        {
            _screen = default;
            if (_target == null)
            {
                _reason = "not-found";
                return false;
            }
            if (!_target.activeInHierarchy)
            {
                _reason = "inactive";
                return false;
            }

            if (_target.transform is RectTransform _rect)
            {
                Canvas _canvas = _target.GetComponentInParent<Canvas>();
                if (_canvas == null || !_canvas.isActiveAndEnabled)
                {
                    _reason = "no-canvas";
                    return false;
                }

                if (IsZeroSize(_rect) && !_target.GetComponentsInChildren<UnityEngine.UI.Graphic>()
                        .Any(_g => _g.raycastTarget && _g.isActiveAndEnabled && !IsZeroSize(_g.rectTransform)))
                {
                    // Collapsed / scaled to nothing with nothing drawn under it (a zero-size container whose children
                    // are drawn, like a card root, is fine: the candidates aim at the children).
                    _reason = "zero-size";
                    return false;
                }
                _screen = RectTransformUtility.WorldToScreenPoint(CanvasCamera(_canvas), _rect.TransformPoint(_rect.rect.center));
            }
            else if (FindCollider(_target, out Collider _collider))
            {
                if (!_collider.enabled)
                {
                    _reason = "disabled";
                    return false;
                }
                Camera _camera = Camera.main;
                if (_camera == null)
                {
                    _reason = "no-camera";
                    return false;
                }
                Vector3 _point = _camera.WorldToScreenPoint(_collider.bounds.center);
                if (_point.z <= 0f)
                {
                    _reason = "behind-camera";
                    return false;
                }
                _screen = _point;
            }
            else
            {
                _reason = "no-rect-or-collider";
                return false;
            }

            if (_screen.x < 0f || _screen.y < 0f || _screen.x > Screen.width || _screen.y > Screen.height)
            {
                _reason = "off-screen";
                return false;
            }

            _reason = null;
            return true;
        }

        private static Camera CanvasCamera(Canvas _canvas)
        {
            Canvas _root = _canvas.rootCanvas;
            return _root.renderMode == RenderMode.ScreenSpaceOverlay ? null : (_root.worldCamera ? _root.worldCamera : Camera.main);
        }

        /// <summary>Where to aim the reticle at <paramref name="_target"/>: a visible point the raycast reaches, else the
        /// centre of its first raycastable graphic (uGUI) or collider bounds. Off-screen points are allowed (the camera
        /// turns to bring them in).</summary>
        public static bool TryAimPoint(GameObject _target, out Vector2 _screen, out string _reason)
        {
            _screen = default;
            if (_target == null || !_target.activeInHierarchy)
            {
                _reason = _target == null ? "not-found" : "inactive";
                return false;
            }

            // A visible point where the real raycast reaches the target (a card's first graphic can be its back face,
            // which raycasts nothing from the front); otherwise its projected centre, to turn towards it.
            Probe _visible = Locate(_target);
            if (_visible.found && string.IsNullOrEmpty(_visible.reason))
            {
                _screen = _visible.screen;
                _reason = null;
                return true;
            }

            Camera _camera;
            Vector3 _world;
            UnityEngine.UI.Graphic _graphic = _target.GetComponentsInChildren<UnityEngine.UI.Graphic>()
                .FirstOrDefault(_g => _g.raycastTarget && _g.isActiveAndEnabled && _g.canvas != null);
            if (_graphic != null)
            {
                _camera = CanvasCamera(_graphic.canvas);
                _world = _graphic.rectTransform.TransformPoint(_graphic.rectTransform.rect.center);
            }
            else if (FindCollider(_target, out Collider _collider) && _collider.enabled)
            {
                _camera = Camera.main;
                _world = _collider.bounds.center;
            }
            else
            {
                _reason = "no-raycast-target";
                return false;
            }

            if (_camera == null)
            {
                _screen = RectTransformUtility.WorldToScreenPoint(null, _world);
                _reason = null;
                return true;
            }
            Vector3 _point = _camera.WorldToScreenPoint(_world);
            if (_point.z <= 0f)
            {
                _reason = "behind-camera";
                return false;
            }
            _screen = _point;
            _reason = null;
            return true;
        }

        /// <summary>True for an element of a screen-space overlay canvas (fixed on screen whatever the camera does).</summary>
        public static bool IsScreenFixed(GameObject _target)
        {
            if (_target == null || !(_target.transform is RectTransform))
            {
                return false;
            }
            Canvas _canvas = _target.GetComponentInParent<Canvas>();
            return _canvas != null && _canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay;
        }

        private static bool IsZeroSize(RectTransform _rect)
            => Mathf.Abs(_rect.rect.width * _rect.lossyScale.x) < 0.01f || Mathf.Abs(_rect.rect.height * _rect.lossyScale.y) < 0.01f;

        // The target's own collider, else the first one below it (a power's collider sits on its spawned model).
        private static bool FindCollider(GameObject _target, out Collider _collider)
        {
            _collider = _target.GetComponent<Collider>();
            if (_collider == null)
            {
                _collider = _target.GetComponentsInChildren<Collider>(true).FirstOrDefault(_c => _c.enabled) ??
                            _target.GetComponentInChildren<Collider>(true);
            }
            return _collider != null;
        }

        private static readonly float[] GridSteps = { 0.5f, 0.25f, 0.75f };
        private static readonly Vector3[] Corners = new Vector3[4];

        // Where a player would click: on what is drawn and raycastable under the target (a button's RectTransform
        // centre can sit on no graphic at all), centre first, then a 3x3 grid, kept on screen.
        private static IEnumerable<Vector2> CandidatePoints(GameObject _target)
        {
            if (FindCollider(_target, out Collider _collider) && _collider.enabled && Camera.main != null)
            {
                Bounds _bounds = _collider.bounds;
                Vector2 _cMin = new(float.MaxValue, float.MaxValue), _cMax = new(float.MinValue, float.MinValue);
                for (int _corner = 0; _corner < 8; _corner++)
                {
                    Vector3 _offset = Vector3.Scale(_bounds.extents, new Vector3((_corner & 1) == 0 ? -1 : 1, (_corner & 2) == 0 ? -1 : 1, (_corner & 4) == 0 ? -1 : 1));
                    Vector3 _p = Camera.main.WorldToScreenPoint(_bounds.center + _offset);
                    _cMin = Vector2.Min(_cMin, _p);
                    _cMax = Vector2.Max(_cMax, _p);
                }
                foreach (float _x in GridSteps)
                {
                    foreach (float _y in GridSteps)
                    {
                        yield return new Vector2(Mathf.Lerp(_cMin.x, _cMax.x, _x), Mathf.Lerp(_cMin.y, _cMax.y, _y));
                    }
                }
            }

            foreach (UnityEngine.UI.Graphic _graphic in _target.GetComponentsInChildren<UnityEngine.UI.Graphic>())
            {
                if (!_graphic.raycastTarget || !_graphic.isActiveAndEnabled || _graphic.canvas == null)
                {
                    continue;
                }

                Camera _camera = CanvasCamera(_graphic.canvas);
                _graphic.rectTransform.GetWorldCorners(Corners);
                Vector2 _a = RectTransformUtility.WorldToScreenPoint(_camera, Corners[0]);
                Vector2 _b = RectTransformUtility.WorldToScreenPoint(_camera, Corners[2]);
                Vector2 _min = Vector2.Max(Vector2.Min(_a, _b), Vector2.one);
                Vector2 _max = Vector2.Min(Vector2.Max(_a, _b), new Vector2(Screen.width - 1, Screen.height - 1));
                if (_min.x > _max.x || _min.y > _max.y)
                {
                    continue; // entirely off screen
                }

                foreach (float _x in GridSteps)
                {
                    foreach (float _y in GridSteps)
                    {
                        yield return new Vector2(Mathf.Lerp(_min.x, _max.x, _x), Mathf.Lerp(_min.y, _max.y, _y));
                    }
                }
            }
        }

        /// <summary>First object the real EventSystem raycasters hit at <paramref name="_screen"/> (null = nothing).</summary>
        public static GameObject TopHit(Vector2 _screen)
        {
            EventSystem _events = EventSystem.current;
            if (_events == null)
            {
                return null;
            }

            Hits.Clear();
            _events.RaycastAll(new PointerEventData(_events) { position = _screen }, Hits);
            return Hits.Count > 0 ? Hits[0].gameObject : null;
        }

        /// <summary>Where to click <paramref name="_target"/> and whether the click would reach it right now.</summary>
        public static Probe Locate(GameObject _target)
        {
            bool _centreOk = TryScreenPoint(_target, out Vector2 _screen, out string _reason);
            if (!_centreOk && _reason != "off-screen")
            {
                return new Probe(false, _screen, null, _reason);
            }

            GameObject _hit = _centreOk ? TopHit(_screen) : null;
            if (_centreOk && Reaches(_hit, _target))
            {
                return new Probe(true, _screen, _hit, null);
            }

            foreach (Vector2 _candidate in CandidatePoints(_target))
            {
                GameObject _candidateHit = TopHit(_candidate);
                if (Reaches(_candidateHit, _target))
                {
                    return new Probe(true, _candidate, _candidateHit, null);
                }
            }
            // Nowhere reaches it: report what covers its centre (the most likely masking element).
            return _centreOk ? new Probe(true, _screen, _hit, "occluded") : new Probe(false, _screen, null, "off-screen");
        }

        /// <summary>
        /// True when the pointer on <paramref name="_hit"/> is on <paramref name="_target"/>: the hit is the target or
        /// part of it (hover-only and press targets such as tooltip triggers and sliders have no click handler), or the
        /// click there is handled by the target or one of its children.
        /// </summary>
        public static bool Reaches(GameObject _hit, GameObject _target)
        {
            if (_hit == null || _target == null)
            {
                return false;
            }
            if (_hit == _target || _hit.transform.IsChildOf(_target.transform))
            {
                return true;
            }

            GameObject _handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hit);
            return _handler != null && (_handler == _target || _handler.transform.IsChildOf(_target.transform));
        }

        /// <summary>
        /// Screen point of a UI Toolkit element, on a screen-space panel (<paramref name="_raw"/> null: HUD overlays such
        /// as the role card) or drawn into a RenderTexture shown on <paramref name="_raw"/> (the tablet apps). The panel's
        /// own screen-to-panel function (RuntimePanelUtils.ScreenToPanel calls it: the presenter's for a RenderTexture,
        /// the panel scaling for an overlay) is inverted numerically, the point is validated by a Pick, and the real
        /// raycast there must reach this panel (or the tablet's RawImage): nothing else covers it.
        /// </summary>
        public static bool TryLocateUitkElement(UnityEngine.UIElements.VisualElement _element, UnityEngine.UI.RawImage _raw, out Vector2 _screen, out string _detail)
        {
            _screen = default;
            if (_element == null || _element.panel == null)
            {
                _detail = "reason=not-found";
                return false;
            }
            if (_raw != null && (_raw.texture == null || !_raw.isActiveAndEnabled))
            {
                _detail = "reason=no-render-texture";
                return false;
            }
            Rect _bound = _element.worldBound;
            if (_element.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None || _bound.width <= 0f || _bound.height <= 0f)
            {
                _detail = "reason=hidden";
                return false;
            }

            Vector2 _target = _bound.center;
            if (float.IsNaN(_target.x) || float.IsNaN(_target.y))
            {
                _detail = "reason=not-laid-out";
                return false;
            }

            // Start from the RawImage's centre on screen (or the screen centre for an overlay panel), then invert the
            // panel's own screen-to-panel function numerically (finite-difference Newton steps): no assumption on
            // scale, axis direction or camera.
            Vector2 _start = new(Screen.width * 0.5f, Screen.height * 0.5f);
            if (_raw != null)
            {
                Canvas _root = _raw.canvas != null ? _raw.canvas.rootCanvas : null;
                Camera _camera = _root == null || _root.renderMode == RenderMode.ScreenSpaceOverlay ? null : (_root.worldCamera ? _root.worldCamera : Camera.main);
                _start = RectTransformUtility.WorldToScreenPoint(_camera, _raw.rectTransform.TransformPoint(_raw.rectTransform.rect.center));
            }
            string _why = "reason=no-solution";
            foreach (bool _topLeft in new[] { true, false })
            {
                if (!TrySolveScreenPoint(_element.panel, _target, _start, _topLeft, out Vector2 _candidate, out string _solveWhy))
                {
                    _why = _solveWhy;
                    continue;
                }
                if (_candidate.x < 0f || _candidate.y < 0f || _candidate.x > Screen.width || _candidate.y > Screen.height)
                {
                    _why = "reason=off-screen";
                    continue;
                }
                Vector2 _back = PanelPoint(_element.panel, _candidate, _topLeft);
                var _picked = _element.panel.Pick(_back);
                if (_picked == null || !(_picked == _element || _element.Contains(_picked)))
                {
                    _why = $"reason=picked-other picked={(_picked == null ? "none" : (string.IsNullOrEmpty(_picked.name) ? _picked.GetType().Name : _picked.name) + "[" + string.Join(".", _picked.GetClasses()) + "]")}";
                    continue;
                }
                GameObject _top = TopHit(_candidate);
                // Reached when the first raycast hit is the tablet's RawImage or this very panel (its event handler);
                // anything else (another canvas, another UI Toolkit panel) covers it.
                bool _reaches = _top != null && ((_raw != null && (_top == _raw.gameObject || _top.transform.IsChildOf(_raw.transform))) ||
                                                 (_top.TryGetComponent(out UnityEngine.UIElements.PanelEventHandler _handler) && _handler.panel == _element.panel));
                if (!_reaches)
                {
                    _why = $"reason=occluded hit={PathOf(_top)}";
                    continue;
                }
                _screen = _candidate;
                _detail = string.Format(CultureInfo.InvariantCulture, "pos={0:0},{1:0} panel={2:0},{3:0}", _candidate.x, _candidate.y, _back.x, _back.y);
                return true;
            }
            _detail = string.Format(CultureInfo.InvariantCulture, "panel={0:0},{1:0} {2}", _target.x, _target.y, _why);
            return false;
        }

        // The panel's screen-to-panel function (the presenter's, through RuntimePanelUtils) for a bottom-left screen
        // point; _topLeft = UI Toolkit expects a y-down screen position.
        private static Vector2 PanelPoint(UnityEngine.UIElements.IPanel _panel, Vector2 _screen, bool _topLeft)
            => UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(_panel, _topLeft ? new Vector2(_screen.x, Screen.height - _screen.y) : _screen);

        private static bool TrySolveScreenPoint(UnityEngine.UIElements.IPanel _panel, Vector2 _target, Vector2 _start, bool _topLeft,
            out Vector2 _screen, out string _why)
        {
            const float Step = 4f;
            _screen = _start;
            for (int _iteration = 0; _iteration < 10; _iteration++)
            {
                Vector2 _value = PanelPoint(_panel, _screen, _topLeft);
                if (float.IsNaN(_value.x))
                {
                    _why = "reason=app-closed";
                    return false;
                }
                Vector2 _error = _target - _value;
                if (_error.magnitude < 0.75f)
                {
                    _why = null;
                    return true;
                }
                Vector2 _dx = (PanelPoint(_panel, _screen + new Vector2(Step, 0f), _topLeft) - _value) / Step;
                Vector2 _dy = (PanelPoint(_panel, _screen + new Vector2(0f, Step), _topLeft) - _value) / Step;
                float _det = _dx.x * _dy.y - _dy.x * _dx.y;
                if (float.IsNaN(_det) || Mathf.Abs(_det) < 1e-6f)
                {
                    _why = "reason=degenerate";
                    return false;
                }
                // Solve [dx dy] * delta = error.
                Vector2 _delta = new((_error.x * _dy.y - _dy.x * _error.y) / _det, (_dx.x * _error.y - _error.x * _dx.y) / _det);
                _screen += Vector2.ClampMagnitude(_delta, 2000f);
            }
            _why = "reason=no-convergence";
            return Vector2.Distance(PanelPoint(_panel, _screen, _topLeft), _target) < 3f;
        }

        /// <summary>A UI Toolkit element of the document on the GameObject named <paramref name="_documentObject"/>.</summary>
        public static UnityEngine.UIElements.VisualElement UitkElement(string _documentObject, System.Func<UnityEngine.UIElements.VisualElement, UnityEngine.UIElements.VisualElement> _find)
        {
            GameObject _go = GameObject.Find(_documentObject);
            UnityEngine.UIElements.UIDocument _document = _go != null ? _go.GetComponent<UnityEngine.UIElements.UIDocument>() : null;
            UnityEngine.UIElements.VisualElement _root = _document != null ? _document.rootVisualElement : null;
            return _root != null ? _find(_root) : null;
        }

        /// <summary>Short description of a UI Toolkit element for the journal: name[classes]:text.</summary>
        public static string DescribeElement(UnityEngine.UIElements.VisualElement _element)
        {
            if (_element == null) return "none";
            string _text = _element is UnityEngine.UIElements.TextElement _textElement ? _textElement.text : null;
            string _name = string.IsNullOrEmpty(_element.name) ? _element.GetType().Name : _element.name;
            return $"{_name}[{string.Join(".", _element.GetClasses())}]{(string.IsNullOrEmpty(_text) ? "" : ":" + _text)}".Replace(' ', '_');
        }

        // A UI Toolkit panel answers uGUI raycasts through its PanelEventHandler GameObject (named after its PanelSettings).
        public static bool IsUitkHit(GameObject _hit) => _hit != null && _hit.GetComponent<UnityEngine.UIElements.PanelEventHandler>() != null;

        /// <summary>Which UI Toolkit element of which document is picked at <paramref name="_screen"/> (screen-space panels).</summary>
        public static string DescribeUitkPick(Vector2 _screen)
        {
            var _out = new System.Text.StringBuilder();
            foreach (var _document in Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsSortMode.None))
            {
                var _root = _document.rootVisualElement;
                if (_root?.panel == null || _document.panelSettings == null || _document.panelSettings.targetTexture != null)
                {
                    continue; // RenderTexture panels are reached through their RawImage, not here
                }
                Vector2 _panelPoint = UnityEngine.UIElements.RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(_screen.x, Screen.height - _screen.y));
                var _picked = _root.panel.Pick(_panelPoint);
                if (_picked == null)
                {
                    continue;
                }
                _out.Append(_out.Length > 0 ? "," : "").Append(_document.name).Append(':')
                    .Append(string.IsNullOrEmpty(_picked.name) ? _picked.GetType().Name : _picked.name)
                    .Append('[').Append(string.Join(".", _picked.GetClasses())).Append(']');
            }
            return _out.Length > 0 ? _out.ToString().Replace(' ', '_') : "none";
        }

        public static string PathOf(GameObject _object)
        {
            if (_object == null)
            {
                return "none";
            }

            string _path = _object.name;
            Transform _parent = _object.transform.parent;
            for (int _i = 0; _i < 2 && _parent != null; _i++, _parent = _parent.parent)
            {
                _path = _parent.name + "/" + _path;
            }
            return _path.Replace(' ', '_');
        }
    }
}
#endif
