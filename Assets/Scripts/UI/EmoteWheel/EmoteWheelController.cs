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
    /// presses the emote key in the embodied first-person view. It builds one slot per <see cref="EmoteSet"/>
    /// entry, laid out clockwise from the top, and highlights the slot the pointer/stick points at. It owns NO
    /// input and NO network: <see cref="EmoteWheelInput"/> opens/closes it, feeds the selected index each frame
    /// (from <see cref="EmoteWheelSelection"/>), and on release reads <see cref="GetEmote"/> to play it.
    ///
    /// Show/hide mirrors <see cref="UI.RoleCard.RoleCardController"/> (guarded init, class-toggle with a
    /// staggered collapse), but the root is NON-modal: it never blocks the world (picking stays Ignore) — the
    /// cursor is locked during the embodied vote and the wheel is a read-only overlay. Visuals reuse the
    /// sampled gold/scrim tokens (design-owned + provisional, see variables.uss).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class EmoteWheelController : MonoBehaviour
    {
        private const string HiddenClass = "cdp-is-hidden";
        private const string CollapsedClass = "cdp-is-collapsed";
        private const string SlotClass = "emote-wheel__slot";
        private const string SlotIconClass = "emote-wheel__slot-icon";
        private const string SlotSelectedClass = "emote-wheel__slot--selected";
        private const string LabelHiddenClass = "emote-wheel__label--hidden";

        // Exit transition length (opacity) + buffer before we collapse (display:none), mirroring the RoleCard.
        private const long ExitCollapseDelayMs = 220;

        // Radial layout (px, within the fixed square wheel container). Provisional — Poyo tunes the feel.
        private const float RingRadius = 190f;
        private const float SlotSize = 120f;

        [SerializeField] private UIDocument document;

        [Tooltip("The emotes offered by the wheel (order = clockwise-from-top). Wire the EmoteSet asset.")]
        [SerializeField] private EmoteSet emoteSet;

        private VisualElement _root;
        private VisualElement _ring;
        private Label _caption;
        private readonly List<VisualElement> _slots = new();
        private bool _initialized;
        private bool _built;
        private int _selected = EmoteWheelSelection.None;

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

            // Non-modal HUD overlay: never intercept world clicks (cursor is locked during the vote anyway).
            _root.pickingMode = PickingMode.Ignore;

            _initialized = true;
        }

        // Build one slot per emote, positioned around the ring clockwise from the top. Idempotent (once).
        private void BuildSlots()
        {
            if (_built || _ring == null || emoteSet == null) return;

            _ring.Clear();
            _slots.Clear();

            int _count = emoteSet.Count;
            for (int _i = 0; _i < _count; _i++)
            {
                EmoteDefinition _emote = emoteSet.Get(_i);

                var _slot = new VisualElement { name = $"slot-{_i}" };
                _slot.AddToClassList(SlotClass);
                _slot.pickingMode = PickingMode.Ignore;

                var _icon = new VisualElement();
                _icon.AddToClassList(SlotIconClass);
                if (_emote != null && _emote.icon != null)
                {
                    _icon.style.backgroundImage = new StyleBackground(_emote.icon);
                }
                _slot.Add(_icon);

                // Absolute-position the slot centre at the sector angle (CW from up), then re-centre the box.
                float _angleDeg = EmoteWheelSelection.SectorCenterAngle(_i, _count);
                float _angleRad = _angleDeg * Mathf.Deg2Rad;
                float _x = Mathf.Sin(_angleRad) * RingRadius;   // +x = right
                float _y = -Mathf.Cos(_angleRad) * RingRadius;  // screen y is down, so up (angle 0) = -y

                _slot.style.position = Position.Absolute;
                _slot.style.left = new Length(50f, LengthUnit.Percent);
                _slot.style.top = new Length(50f, LengthUnit.Percent);
                _slot.style.translate = new StyleTranslate(
                    new Translate(_x - SlotSize / 2f, _y - SlotSize / 2f));

                _ring.Add(_slot);
                _slots.Add(_slot);
            }

            _built = true;
        }

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
        /// Highlight the slot at <paramref name="index"/> (<see cref="EmoteWheelSelection.None"/> = clear) and
        /// update the caption with that emote's name. Cheap + idempotent — safe to call every frame.
        /// </summary>
        public void SetSelection(int index)
        {
            if (index == _selected) return;
            _selected = index;

            for (int _i = 0; _i < _slots.Count; _i++)
            {
                _slots[_i].EnableInClassList(SlotSelectedClass, _i == index);
            }

            if (_caption != null)
            {
                EmoteDefinition _emote = GetEmote(index);
                bool _has = _emote != null && !string.IsNullOrEmpty(_emote.displayName);
                _caption.text = _has ? _emote.displayName : string.Empty;
                _caption.EnableInClassList(LabelHiddenClass, !_has);
            }
        }
    }
}
