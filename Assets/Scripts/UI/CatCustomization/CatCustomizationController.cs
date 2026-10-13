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
    /// player's cat on the left, one tab per part on the right, the vignettes of that part in a scrolling grid. A
    /// click on a vignette dresses the cat and saves the look at once (no confirm step): the next game sends it.
    /// Échap or « Menu » closes it. Opened by clicking the cat of the menu (MainMenu).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CatCustomizationController : MonoBehaviour
    {
        private const string HiddenClass = "cdp-is-hidden";
        private const string CollapsedClass = "cdp-is-collapsed";
        private const long CollapseDelayMs = 260;
        private const float DragDegreesPerPixel = 0.45f;

        [SerializeField] private UIDocument document;

        [Tooltip("Every cosmetic of the cat, by part.")]
        [SerializeField] private CatCosmeticsCatalog catalog;

        [Tooltip("The menu's cat, filmed into a texture (shown here big, and small on the menu).")]
        [SerializeField] private CatPreviewStage stage;

        private VisualElement _root;
        private VisualElement _catView;
        private VisualElement _tabs;
        private Label _current;
        private VisualElement _cells;
        private CatSlot _openSlot = CatSlot.Fur;
        private CatLook _look;
        private bool _initialized;
        private bool _open;
        private bool _dragging;
        private float _lastDragX;

        public bool IsOpen => _open;
        public CatPreviewStage Stage => stage;
        public CatCosmeticsCatalog Catalog => catalog;

        /// <summary>Raised when the screen opens / closes (the main menu hides its own buttons meanwhile).</summary>
        public event Action Opened;
        public event Action Closed;

        /// <summary>Player-facing names of the parts, in tab order.</summary>
        public static string SlotLabel(CatSlot slot)
        {
            switch (slot)
            {
                case CatSlot.Fur: return "Pelage";
                case CatSlot.Outfit: return "Tenue";
                case CatSlot.Eyes: return "Yeux";
                case CatSlot.Mouth: return "Bouche";
                default: return slot.ToString();
            }
        }

        private void OnEnable() => TryInitialize();

        // UIDocument builds its tree in its own OnEnable (order not guaranteed): Start is the fallback.
        private void Start() => TryInitialize();

        private void TryInitialize()
        {
            if (_initialized) return;
            if (document == null) document = GetComponent<UIDocument>();
            _root = document != null ? document.rootVisualElement?.Q<VisualElement>("cat-custo") : null;
            if (_root == null) return;

            _catView = _root.Q<VisualElement>("cat-view");
            _tabs = _root.Q<VisualElement>("cat-tabs");
            _current = _root.Q<Label>("cat-current");
            _cells = _root.Q<VisualElement>("cat-cells");
            _root.Q<Label>("cat-back").RegisterCallback<ClickEvent>(_ => Close());

            _catView.RegisterCallback<PointerDownEvent>(OnCatPointerDown);
            _catView.RegisterCallback<PointerMoveEvent>(OnCatPointerMove);
            _catView.RegisterCallback<PointerUpEvent>(OnCatPointerUp);

            _root.pickingMode = PickingMode.Ignore;
            _initialized = true;
        }

        // ------------------------------------------------------------------------------------------ open / close

        public void Open()
        {
            TryInitialize();
            if (_root == null || catalog == null || _open) return;

            _open = true;
            _look = catalog.Sanitize(CatLookStore.Load());
            if (stage != null)
            {
                stage.Apply(_look);
                stage.ResetYaw();
                _catView.style.backgroundImage = Background.FromRenderTexture(stage.Texture);
            }
            Rebuild();
            _root.RemoveFromClassList(CollapsedClass);
            _root.pickingMode = PickingMode.Position;
            _root.schedule.Execute(() => _root.RemoveFromClassList(HiddenClass));
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _dragging = false;
            _root.AddToClassList(HiddenClass);
            _root.pickingMode = PickingMode.Ignore;
            _root.schedule.Execute(() =>
            {
                if (!_open) _root.AddToClassList(CollapsedClass);
            }).ExecuteLater(CollapseDelayMs);
            if (stage != null)
            {
                stage.ResetYaw();
            }
            Closed?.Invoke();
        }

        private void Update()
        {
            if (!_open) return;
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
        }

        // ------------------------------------------------------------------------------------------ tabs + grid

        /// <summary>Opens a part's tab (also used by the autoplay tour).</summary>
        public void ShowSlot(CatSlot slot)
        {
            _openSlot = slot;
            Rebuild();
        }

        /// <summary>Picks an item: the cat wears it at once and the look is saved.</summary>
        public void Choose(CatSlot slot, byte id)
        {
            if (catalog == null || !catalog.Contains(slot, id)) return;
            _look = _look.With(slot, id);
            CatLookStore.Save(_look); // the stage follows through CatLookStore.Changed
            Rebuild();
        }

        private void Rebuild()
        {
            if (_root == null || catalog == null) return;

            _tabs.Clear();
            foreach (CatSlot slot in CatLook.Slots)
            {
                if (catalog.ItemsOf(slot).Count == 0) continue;
                var tab = new Label(SlotLabel(slot));
                tab.AddToClassList("cat-custo__tab");
                tab.EnableInClassList("cat-custo__tab--on", slot == _openSlot);
                CatSlot captured = slot;
                tab.RegisterCallback<ClickEvent>(_ => ShowSlot(captured));
                _tabs.Add(tab);
            }

            CatCosmeticItem chosen = catalog.Find(_openSlot, _look.Get(_openSlot));
            _current.text = chosen != null ? chosen.displayName : string.Empty;

            _cells.Clear();
            foreach (CatCosmeticItem item in catalog.ItemsOf(_openSlot))
            {
                _cells.Add(BuildCell(item, item == chosen));
            }
        }

        private VisualElement BuildCell(CatCosmeticItem item, bool on)
        {
            var cell = new VisualElement();
            cell.AddToClassList("cat-custo__cell");
            cell.EnableInClassList("cat-custo__cell--on", on);

            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("cat-custo__cell-box");
            if (item.icon != null)
            {
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("cat-custo__cell-icon");
                icon.style.backgroundImage = new StyleBackground(item.icon);
                box.Add(icon);
            }
            else
            {
                var none = new Label("-") { pickingMode = PickingMode.Ignore };
                none.AddToClassList("cat-custo__cell-none");
                box.Add(none);
            }
            cell.Add(box);

            var name = new Label(item.displayName) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("cat-custo__cell-name");
            cell.Add(name);

            CatSlot slot = item.slot;
            byte id = item.id;
            cell.RegisterCallback<ClickEvent>(_ => Choose(slot, id));
            return cell;
        }

        // ------------------------------------------------------------------------------------------ turn the cat

        private void OnCatPointerDown(PointerDownEvent evt)
        {
            _dragging = true;
            _lastDragX = evt.position.x;
            _catView.CapturePointer(evt.pointerId);
        }

        private void OnCatPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging || stage == null) return;
            stage.AddYaw(-(evt.position.x - _lastDragX) * DragDegreesPerPixel);
            _lastDragX = evt.position.x;
        }

        private void OnCatPointerUp(PointerUpEvent evt)
        {
            _dragging = false;
            _catView.ReleasePointer(evt.pointerId);
        }
    }
}
