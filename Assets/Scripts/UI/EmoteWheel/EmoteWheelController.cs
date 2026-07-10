#region

using System.Collections.Generic;
using Avatars;
using UnityEngine;
using UnityEngine.UIElements;

#endregion

namespace UI.EmoteWheel
{
    /// <summary>
    /// Drives the radial emote wheel (EmoteWheel.uxml) — a transient HUD overlay held open while the player
    /// presses the emote key in the embodied first-person view. It draws a DONUT (an annular band split into one
    /// sector per <see cref="EmoteSet"/> entry, clockwise from the top) with <see cref="Painter2D"/>, lays an
    /// icon + optional label over each sector, and shows the pointed-at emote's name big in the hole. It owns NO
    /// input and NO network: <see cref="EmoteWheelInput"/> opens/closes it, feeds the selected index each frame
    /// (from <see cref="EmoteWheelSelection"/>), and on release reads <see cref="GetEmote"/> to play it.
    ///
    /// Show/hide mirrors <see cref="UI.RoleCard.RoleCardController"/> (guarded init, class-toggle with a
    /// staggered collapse), but the root is NON-modal: it never blocks the world (picking stays Ignore) — the
    /// cursor is locked during the embodied vote and the wheel is a read-only overlay. Band / separator /
    /// highlight colours are serialized (design-owned + provisional — tune in the Inspector, no USS edit).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class EmoteWheelController : MonoBehaviour
    {
        private const string HiddenClass = "cdp-is-hidden";
        private const string CollapsedClass = "cdp-is-collapsed";
        private const string DonutClass = "emote-wheel__donut";
        private const string SlotClass = "emote-wheel__slot";
        private const string SlotIconClass = "emote-wheel__slot-icon";
        private const string SlotIconPlaceholderClass = "emote-wheel__slot-icon--placeholder";
        private const string SlotLabelClass = "emote-wheel__slot-label";
        private const string SlotSelectedClass = "emote-wheel__slot--selected";
        private const string CaptionHiddenClass = "emote-wheel__label--hidden";

        // Exit transition length (opacity) + buffer before we collapse (display:none), mirroring the RoleCard.
        private const long ExitCollapseDelayMs = 220;

        // Radial layout (px). The donut fills the fixed square ring container (see EmoteWheel.uss); the icons ride
        // the mid-band radius, the labels sit OUTSIDE the outer edge (positioned radially, consistent per sector).
        // Provisional — Poyo tunes the feel. Keep in sync with EmoteWheel.uss (ring/slot/label sizes).
        private const float IconRadius = 232f;
        private const float SlotSize = 84f;
        // Labels sit OUTSIDE the band. Each is pushed out along its ray so its INNER edge lands at
        // OuterRadius + LabelGap whatever the angle — side labels go far (room to spare), top/bottom stay near
        // (no vertical clip). OuterRadius must track the ring (width/2 - separatorWidth ~= 620/2 - 3).
        private const float OuterRadius = 307f;
        private const float LabelGap = 12f;

        [SerializeField] private UIDocument document;

        [Tooltip("The emotes offered by the wheel (order = clockwise-from-top). Wire the EmoteSet asset.")]
        [SerializeField] private EmoteSet emoteSet;

        [Header("Labels")]
        [Tooltip("Show each emote's name under its icon in the ring. Keep ON while icons are placeholder; turn " +
                 "OFF once real icons read clearly (the centre caption still names the hovered emote).")]
        [SerializeField] private bool showSectorLabels = true;

        [Header("Donut colours — provisional, design-owned (tune here, no USS edit)")]
        [Tooltip("Fill of a resting sector's band (alpha lets the game show through).")]
        [SerializeField] private Color bandColor = new(0.05f, 0.05f, 0.07f, 0.55f);

        [Tooltip("Fill of the hovered/selected sector's band.")]
        [SerializeField] private Color bandHighlightColor = new(0.85f, 0.70f, 0.25f, 0.80f);

        [Tooltip("Sector separators + ring outlines.")]
        [SerializeField] private Color separatorColor = new(0.95f, 0.95f, 0.95f, 0.55f);

        [Tooltip("Inner-hole radius as a fraction of the outer radius (donut thickness).")]
        [SerializeField, Range(0.2f, 0.85f)] private float innerRatio = 0.55f;

        [SerializeField, Range(1f, 8f)] private float separatorWidth = 3f;

        [Tooltip("Seconds for a sector's band to fade between rest and highlight colour on hover (0 = instant).")]
        [SerializeField, Range(0f, 0.5f)] private float highlightFadeSeconds = 0.10f;

        [Tooltip("On release, the chosen emote (icon + name) pops in at the centre and fades — a first-person " +
                 "confirmation of what you played. This is its total duration (0 = no confirmation).")]
        [SerializeField, Range(0f, 2f)] private float confirmSeconds = 0.9f;

        private VisualElement _root;
        private VisualElement _ring;
        private VisualElement _donut;
        private Label _caption;
        private readonly List<VisualElement> _slots = new();
        private readonly List<Label> _labels = new();

        // Release confirmation: a top-level icon+name (SIBLING of the wheel root, so the wheel collapsing on
        // release doesn't take it down) that animates from the chosen sector to the centre and fades slowly.
        private VisualElement _confirm;
        private VisualElement _confirmIcon;
        private Label _confirmLabel;
        private IVisualElementScheduledItem _confirmAnim;
        private float _confirmT;
        private const float ConfirmBox = 96f;
        private bool _initialized;
        private bool _built;
        private int _selected = EmoteWheelSelection.None;

        // Snapshot the donut painter reads each repaint (the callback can't see instance selection state cheaply).
        private int _donutCount;
        private int _donutSelected = EmoteWheelSelection.None;

        // Per-sector highlight amount [0..1] the painter lerps the band colour with; animated toward the target
        // (selected = 1, rest = 0) by a paused-when-idle scheduler so the hover colour fades instead of snapping.
        private float[] _sectorT;
        private IVisualElementScheduledItem _bandAnim;

        /// <summary>Number of emotes in the wheel (0 if unwired) — <see cref="EmoteWheelInput"/> reads it for the selection math.</summary>
        public int Count => emoteSet != null ? emoteSet.Count : 0;

        /// <summary>The emote at <paramref name="index"/>, or null if out of range / unwired.</summary>
        public EmoteDefinition GetEmote(int index) => emoteSet != null ? emoteSet.Get(index) : null;

        private void OnEnable() => TryInitialize();

        // Fallback: the UIDocument builds its tree in its OWN OnEnable; order vs this component is not guaranteed.
        private void Start() => TryInitialize();

        private void TryInitialize()
        {
            if (_initialized) return;
            if (document == null) document = GetComponent<UIDocument>();

            var _tree = document != null ? document.rootVisualElement : null;
            _root = _tree?.Q<VisualElement>("emote-wheel");
            if (_root == null) return;

            _ring = _root.Q<VisualElement>("ring");
            _caption = _root.Q<Label>("caption");

            // Non-modal HUD overlay: never intercept world clicks (cursor is locked during the vote anyway). The
            // caption anchor spans the whole ring, so it must not pick either.
            _root.pickingMode = PickingMode.Ignore;
            if (_caption != null)
            {
                _caption.pickingMode = PickingMode.Ignore;
                if (_caption.parent != null) _caption.parent.pickingMode = PickingMode.Ignore;
            }

            EnsureConfirm(_tree);

            _initialized = true;
        }

        // Build the donut painter (behind) + one icon/label slot per emote around the ring. Idempotent (once).
        private void BuildSlots()
        {
            if (_built || _ring == null || emoteSet == null) return;

            EnsureDonut();

            // Clear any prior slots + labels (keep the donut + caption which live in the ring).
            foreach (VisualElement _slot in _slots) _slot.RemoveFromHierarchy();
            foreach (Label _label in _labels) _label.RemoveFromHierarchy();
            _slots.Clear();
            _labels.Clear();

            int _count = emoteSet.Count;
            for (int _i = 0; _i < _count; _i++)
            {
                EmoteDefinition _emote = emoteSet.Get(_i);
                float _angleRad = EmoteWheelSelection.SectorCenterAngle(_i, _count) * Mathf.Deg2Rad;
                float _sin = Mathf.Sin(_angleRad);   // +x = right
                float _cos = -Mathf.Cos(_angleRad);  // screen y is down, so up (angle 0) = -y

                // --- Icon slot: rides the mid-band radius, box re-centred on the point. ---
                var _slot = new VisualElement { name = $"slot-{_i}" };
                _slot.AddToClassList(SlotClass);
                _slot.pickingMode = PickingMode.Ignore;

                var _icon = new VisualElement();
                _icon.AddToClassList(SlotIconClass);
                _icon.pickingMode = PickingMode.Ignore;
                if (_emote != null && _emote.icon != null)
                {
                    _icon.style.backgroundImage = new StyleBackground(_emote.icon);
                }
                else
                {
                    // No sprite yet → a rotated square placeholder (the sketch's diamond).
                    _icon.AddToClassList(SlotIconPlaceholderClass);
                }
                _slot.Add(_icon);

                PlaceAbsolute(_slot, _sin * IconRadius, _cos * IconRadius, SlotSize, SlotSize);
                _ring.Add(_slot);
                _slots.Add(_slot);

                // --- Label: OUTSIDE the outer edge, positioned radially (consistent for every sector). ---
                if (showSectorLabels)
                {
                    var _label = new Label(_emote != null ? _emote.displayName : string.Empty);
                    _label.AddToClassList(SlotLabelClass);
                    _label.pickingMode = PickingMode.Ignore;
                    _label.style.position = Position.Absolute;
                    _label.style.left = new Length(50f, LengthUnit.Percent);
                    _label.style.top = new Length(50f, LengthUnit.Percent);
                    // The box hugs the text (auto width), so its resolved size is only known after layout — place
                    // it (uniform inner-edge gap, whatever the text width) on the geometry event. Capture the ray.
                    float _labelSin = _sin;
                    float _labelCos = _cos;
                    _label.RegisterCallback<GeometryChangedEvent>(_ => PositionLabel(_label, _labelSin, _labelCos));
                    _ring.Add(_label);
                    _labels.Add(_label);
                }
            }

            // Keep the caption (its full-ring anchor) on top of the freshly-added slots.
            (_caption?.parent ?? _caption)?.BringToFront();

            _donutCount = _count;
            _donutSelected = EmoteWheelSelection.None;
            _sectorT = new float[_count];
            _bandAnim ??= _donut.schedule.Execute(TickBandFade).Every(16);
            _bandAnim.Pause();
            _donut?.MarkDirtyRepaint();

            _built = true;
        }

        // Absolute-place an element so its CENTRE lands at (dx, dy) px from the ring centre (50%/50% origin),
        // given its fixed box size. Keeps icons + radial labels on their sector's ray.
        private static void PlaceAbsolute(VisualElement element, float dx, float dy, float width, float height)
        {
            element.style.position = Position.Absolute;
            element.style.left = new Length(50f, LengthUnit.Percent);
            element.style.top = new Length(50f, LengthUnit.Percent);
            element.style.translate = new StyleTranslate(new Translate(dx - width / 2f, dy - height / 2f));
        }

        // Place a radial label so its box's INNER edge sits a uniform LabelGap outside the band, projecting the
        // (now-known) box half-size onto the ray. Auto-width means the visible gap is the same for "yo" and
        // "a l'aide !" alike. Called on each GeometryChangedEvent (size resolves after layout).
        private static void PositionLabel(VisualElement label, float sin, float cos)
        {
            float _w = label.resolvedStyle.width;
            float _h = label.resolvedStyle.height;
            if (_w <= 0f || _h <= 0f) return;

            float _r = OuterRadius + LabelGap + (Mathf.Abs(sin) * _w + Mathf.Abs(cos) * _h) * 0.5f;
            label.style.translate = new StyleTranslate(new Translate(sin * _r - _w / 2f, cos * _r - _h / 2f));
        }

        // Build the release-confirmation element once, as a SIBLING of the wheel root on the panel tree so the
        // wheel collapsing on release never hides it. Centred origin (left/top 50%); the tween drives translate.
        private void EnsureConfirm(VisualElement tree)
        {
            if (_confirm != null || tree == null) return;

            _confirm = new VisualElement { name = "emote-confirm" };
            _confirm.AddToClassList("emote-confirm");
            _confirm.pickingMode = PickingMode.Ignore;
            _confirm.style.display = DisplayStyle.None;

            _confirmIcon = new VisualElement();
            _confirmIcon.AddToClassList(SlotIconClass);
            _confirmIcon.pickingMode = PickingMode.Ignore;
            _confirm.Add(_confirmIcon);

            _confirmLabel = new Label();
            _confirmLabel.AddToClassList("emote-confirm__label");
            _confirmLabel.pickingMode = PickingMode.Ignore;
            _confirm.Add(_confirmLabel);

            tree.Add(_confirm);
        }

        /// <summary>
        /// Play the "you emoted X" confirmation: the chosen emote's icon + name POP in at the centre and fade out
        /// over <see cref="confirmSeconds"/> — a first-person cue for what was just played. No-op when disabled
        /// (0s), unbuilt, or the index is empty.
        /// </summary>
        public void Confirm(int index)
        {
            if (confirmSeconds <= 0f || _confirm == null) return;
            EmoteDefinition _emote = GetEmote(index);
            if (_emote == null) return;

            bool _placeholder = _emote.icon == null;
            _confirmIcon.EnableInClassList(SlotIconPlaceholderClass, _placeholder);
            _confirmIcon.style.backgroundImage = _placeholder
                ? StyleKeyword.None
                : new StyleBackground(_emote.icon);
            _confirmLabel.text = _emote.displayName ?? string.Empty;

            _confirmT = 0f;
            _confirm.style.display = DisplayStyle.Flex;
            ApplyConfirmFrame();

            _confirmAnim ??= _confirm.schedule.Execute(TickConfirm).Every(16);
            _confirmAnim.Resume();
        }

        private void TickConfirm(TimerState ts)
        {
            if (_confirm == null)
            {
                _confirmAnim?.Pause();
                return;
            }

            _confirmT += confirmSeconds <= 0f ? 1f : ts.deltaTime / 1000f / confirmSeconds;
            if (_confirmT >= 1f)
            {
                _confirmT = 1f;
                _confirm.style.display = DisplayStyle.None;
                _confirmAnim?.Pause();
                return;
            }
            ApplyConfirmFrame();
        }

        // Position/scale/opacity for the current confirmation progress: POP in at the centre (a quick scale-up
        // with a slight overshoot that settles), hold, then fade out. Fixed at the centre — no travel.
        private void ApplyConfirmFrame()
        {
            // Pop: 0.7 -> 1.1 (0..0.15), 1.1 -> 1.0 (0.15..0.28), steady after.
            float _scale;
            if (_confirmT < 0.15f) _scale = Mathf.Lerp(0.7f, 1.1f, _confirmT / 0.15f);
            else if (_confirmT < 0.28f) _scale = Mathf.Lerp(1.1f, 1.0f, (_confirmT - 0.15f) / 0.13f);
            else _scale = 1.0f;

            // Fade in fast, hold, fade out over the tail.
            float _alphaIn = Mathf.InverseLerp(0f, 0.1f, _confirmT);
            float _alphaOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, _confirmT));
            float _alpha = Mathf.Min(_alphaIn, _alphaOut);

            _confirm.style.translate = new StyleTranslate(new Translate(-ConfirmBox / 2f, -ConfirmBox / 2f));
            _confirm.style.scale = new StyleScale(new Scale(new Vector2(_scale, _scale)));
            _confirm.style.opacity = _alpha;
        }

        // Create the custom donut element once and insert it behind everything in the ring.
        private void EnsureDonut()
        {
            if (_donut != null || _ring == null) return;

            _donut = new VisualElement { name = "donut" };
            _donut.AddToClassList(DonutClass);
            _donut.pickingMode = PickingMode.Ignore;
            _donut.generateVisualContent += OnGenerateDonut;
            _ring.Insert(0, _donut);
        }

        // Painter2D: draw one filled annular sector per emote (highlighting the selected one), the radial
        // separators between sectors, and the inner/outer ring outlines. Reads the _donut* snapshot.
        private void OnGenerateDonut(MeshGenerationContext mgc)
        {
            Rect _rect = mgc.visualElement.contentRect;
            if (_rect.width < 2f || _rect.height < 2f) return;

            Vector2 _center = _rect.center;
            float _rOut = Mathf.Min(_rect.width, _rect.height) * 0.5f - separatorWidth;
            float _rIn = _rOut * innerRatio;
            int _n = Mathf.Max(_donutCount, 1);
            Painter2D _p = mgc.painter2D;

            // Filled annular sectors.
            for (int _i = 0; _i < _n; _i++)
            {
                // A single sector spans a full turn: use DISTINCT 0/360 endpoints (a centre±180 span gives
                // -270/+90, which normalize to the same angle and collapse the fill arc into a wedge). n>1
                // sectors are < 360 with distinct endpoints, so they take the normal centre±half span.
                float _a0, _a1;
                if (_n == 1)
                {
                    _a0 = 0f;
                    _a1 = 360f;
                }
                else
                {
                    float _centerDeg = SectorCenterScreenDeg(_i, _n);
                    float _half = 180f / _n;
                    _a0 = _centerDeg - _half;
                    _a1 = _centerDeg + _half;
                }

                float _t = _sectorT != null && _i < _sectorT.Length ? _sectorT[_i] : 0f;
                _p.fillColor = Color.Lerp(bandColor, bandHighlightColor, _t);
                FillAnnularSector(_p, _center, _rOut, _rIn, _a0, _a1);
            }

            // Radial separators (only meaningful with more than one sector).
            _p.strokeColor = separatorColor;
            _p.lineWidth = separatorWidth;
            if (_n > 1)
            {
                for (int _i = 0; _i < _n; _i++)
                {
                    float _edgeRad = (SectorCenterScreenDeg(_i, _n) - 180f / _n) * Mathf.Deg2Rad;
                    var _dir = new Vector2(Mathf.Cos(_edgeRad), Mathf.Sin(_edgeRad));
                    _p.BeginPath();
                    _p.MoveTo(_center + _dir * _rIn);
                    _p.LineTo(_center + _dir * _rOut);
                    _p.Stroke();
                }
            }

            // Inner + outer ring outlines.
            DrawCircle(_p, _center, _rOut);
            DrawCircle(_p, _center, _rIn);
        }

        // Advance every sector's highlight amount toward its target (selected = 1, rest = 0), repaint, and pause
        // once settled. Framerate-independent (uses the tick's delta). Cheap — only runs mid-transition.
        private void TickBandFade(TimerState ts)
        {
            if (_sectorT == null || _donut == null)
            {
                _bandAnim?.Pause();
                return;
            }

            float _dt = ts.deltaTime / 1000f;
            float _step = highlightFadeSeconds <= 0f ? 1f : _dt / highlightFadeSeconds;
            bool _moving = false;

            for (int _i = 0; _i < _sectorT.Length; _i++)
            {
                float _target = _i == _donutSelected ? 1f : 0f;
                if (!Mathf.Approximately(_sectorT[_i], _target))
                {
                    _sectorT[_i] = Mathf.MoveTowards(_sectorT[_i], _target, _step);
                    _moving = true;
                }
            }

            _donut.MarkDirtyRepaint();
            if (!_moving) _bandAnim?.Pause();
        }

        // Fill one annular sector [a0..a1] (deg), subdivided so no sub-arc exceeds ~90°. A single Painter2D arc
        // spanning a large sweep (esp. a full 360° ring) mis-tessellates and leaves a triangular gap; the small
        // <=90° annular quads (the exact geometry the multi-emote wheel already fills cleanly) don't. Adjacent
        // sub-fills share coincident edges and the same colour, so the band reads seamless.
        private static void FillAnnularSector(Painter2D p, Vector2 center, float rOut, float rIn, float a0, float a1)
        {
            float _sweep = a1 - a0;
            int _steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(_sweep) / 90f));
            float _d = _sweep / _steps;

            for (int _s = 0; _s < _steps; _s++)
            {
                float _b0 = a0 + _d * _s;
                float _b1 = a0 + _d * (_s + 1);
                p.BeginPath();
                p.Arc(center, rOut, new Angle(_b0, AngleUnit.Degree), new Angle(_b1, AngleUnit.Degree),
                    ArcDirection.Clockwise);
                p.Arc(center, rIn, new Angle(_b1, AngleUnit.Degree), new Angle(_b0, AngleUnit.Degree),
                    ArcDirection.CounterClockwise);
                p.ClosePath();
                p.Fill();
            }
        }

        private static void DrawCircle(Painter2D p, Vector2 center, float radius)
        {
            p.BeginPath();
            p.Arc(center, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree),
                ArcDirection.Clockwise);
            p.Stroke();
        }

        // Sector-centre angle in Painter2D screen space (deg, 0 = +x, y-down => clockwise). The selection math is
        // CW-from-up, so screen = that minus 90°.
        private static float SectorCenterScreenDeg(int index, int count) =>
            EmoteWheelSelection.SectorCenterAngle(index, count) - 90f;

        /// <summary>Build (once) + reveal the wheel with no slot selected.</summary>
        public void Open()
        {
            TryInitialize();
            if (_root == null) return;

            BuildSlots();
            SetSelection(EmoteWheelSelection.None);

            _root.RemoveFromClassList(CollapsedClass);
            // Drop the fade class next frame so the 0→1 opacity transition runs.
            _root.schedule.Execute(() => _root.RemoveFromClassList(HiddenClass));
        }

        /// <summary>Animate the wheel out; it collapses (display:none) once the exit finishes.</summary>
        public void Close()
        {
            if (_root == null) return;
            _root.AddToClassList(HiddenClass);
            _root.schedule.Execute(CollapseIfHidden).ExecuteLater(ExitCollapseDelayMs);
        }

        private void CollapseIfHidden()
        {
            if (_root != null && _root.ClassListContains(HiddenClass)) _root.AddToClassList(CollapsedClass);
        }

        /// <summary>
        /// Highlight the sector at <paramref name="index"/> (<see cref="EmoteWheelSelection.None"/> = clear),
        /// repaint the donut, scale the icon, and show that emote's name big in the hole. Cheap + idempotent —
        /// safe to call every frame.
        /// </summary>
        public void SetSelection(int index)
        {
            if (index == _selected) return;
            _selected = index;

            for (int _i = 0; _i < _slots.Count; _i++)
            {
                _slots[_i].EnableInClassList(SlotSelectedClass, _i == index);
            }

            // Hide the selected sector's own label — the centre caption names it, so no duplicate.
            for (int _i = 0; _i < _labels.Count; _i++)
            {
                _labels[_i].EnableInClassList(CaptionHiddenClass, _i == index);
            }

            _donutSelected = index;
            if (highlightFadeSeconds <= 0f)
            {
                if (_sectorT != null)
                    for (int _i = 0; _i < _sectorT.Length; _i++) _sectorT[_i] = _i == index ? 1f : 0f;
                _donut?.MarkDirtyRepaint();
            }
            else
            {
                _bandAnim?.Resume();
            }

            if (_caption != null)
            {
                EmoteDefinition _emote = GetEmote(index);
                bool _has = _emote != null && !string.IsNullOrEmpty(_emote.displayName);
                _caption.text = _has ? _emote.displayName : string.Empty;
                _caption.EnableInClassList(CaptionHiddenClass, !_has);
            }
        }
    }
}
