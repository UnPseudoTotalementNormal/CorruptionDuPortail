using System;
using Avatars.Cosmetics;
using CorruptionDuPortail.Domain;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace UI.CatCustomization
{
    /// <summary>
    /// The main menu's cat customization screen (CatCustomization.uxml, approved mockup personnalisation v2 C1): the
    /// player's cat on the left, one tab per part on the right, the vignettes of that part in a scrolling grid
    /// (<see cref="CatCustomizationPanel"/>, shared with the lobby tablet). Échap or « Menu » closes it.
    /// Opened by clicking the cat of the menu (MainMenu).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CatCustomizationController : MonoBehaviour
    {
        private const string HiddenClass = "cdp-is-hidden";
        private const string CollapsedClass = "cdp-is-collapsed";
        private const long CollapseDelayMs = 260;

        [SerializeField] private UIDocument document;

        [Tooltip("Every cosmetic of the cat, by part.")]
        [SerializeField] private CatCosmeticsCatalog catalog;

        [Tooltip("The menu's cat, filmed into a texture (shown here big, and small on the menu).")]
        [SerializeField] private CatPreviewStage stage;

        private VisualElement _root;
        private CatCustomizationPanel _panel;
        private bool _initialized;
        private bool _open;

        public bool IsOpen => _open;
        public CatPreviewStage Stage => stage;
        public CatCosmeticsCatalog Catalog => catalog;

        /// <summary>Raised when the screen opens / closes (the main menu hides its own buttons meanwhile).</summary>
        public event Action Opened;
        public event Action Closed;

        private void OnEnable() => TryInitialize();

        // UIDocument builds its tree in its own OnEnable (order not guaranteed): Start is the fallback.
        private void Start() => TryInitialize();

        private void TryInitialize()
        {
            if (_initialized) return;
            if (document == null) document = GetComponent<UIDocument>();
            _root = document != null ? document.rootVisualElement?.Q<VisualElement>("cat-custo") : null;
            if (_root == null) return;

            _panel = new CatCustomizationPanel(catalog, stage, _root.Q<VisualElement>("cat-view"),
                _root.Q<VisualElement>("cat-tabs"), _root.Q<Label>("cat-current"), _root.Q<VisualElement>("cat-cells"));
            _root.Q<Label>("cat-back").RegisterCallback<ClickEvent>(_ => Close());

            _root.pickingMode = PickingMode.Ignore;
            _initialized = true;
        }

        // ------------------------------------------------------------------------------------------ open / close

        public void Open()
        {
            TryInitialize();
            if (_root == null || catalog == null || _open)
            {
                if (!_open) Diagnostics.Logs.GameLog.Warn("ui", "ui.cosmetic.refused", "reason", _root == null ? "no-root" : "no-catalog");
                return;
            }

            Diagnostics.Logs.GameLog.Info("ui", "ui.cosmetic.open", "where", "menu");
            _open = true;
            _panel.Open();
            _root.RemoveFromClassList(CollapsedClass);
            _root.pickingMode = PickingMode.Position;
            _root.schedule.Execute(() => _root.RemoveFromClassList(HiddenClass));
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!_open) return;
            Diagnostics.Logs.GameLog.Info("ui", "ui.cosmetic.close", "where", "menu");
            _open = false;
            _panel.Close();
            _root.AddToClassList(HiddenClass);
            _root.pickingMode = PickingMode.Ignore;
            _root.schedule.Execute(() =>
            {
                if (!_open) _root.AddToClassList(CollapsedClass);
            }).ExecuteLater(CollapseDelayMs);
            Closed?.Invoke();
        }

        private void Update()
        {
            if (!_open) return;
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
            _panel.Tick();
        }

        /// <summary>Opens a part's tab (also used by the autoplay tour).</summary>
        public void ShowSlot(CatSlot slot) => _panel.ShowSlot(slot);

        /// <summary>Picks an item: the cat wears it at once and the look is saved.</summary>
        public void Choose(CatSlot slot, byte id) => _panel.Choose(slot, id);

        /// <summary>Sets the cat spinning as if thrown at <paramref name="degreesPerSecond"/> (autoplay demo).</summary>
        public void Throw(float degreesPerSecond) => _panel.Throw(degreesPerSecond);

        public bool IsSpinning => _panel != null && _panel.IsSpinning;
    }
}
