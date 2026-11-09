using System;
using Avatars.Cosmetics;
using CorruptionDuPortail.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI.CatCustomization
{
    /// <summary>
    /// What the main menu's screen (<see cref="CatCustomizationController"/>) and the lobby tablet's « Mon chat » tab
    /// (LobbyRolesUitkController) share: the cat that turns when dragged and keeps spinning when thrown, one tab per
    /// part, the chosen item's name, the grid of vignettes. A click on a vignette dresses the cat and saves the look at
    /// once (no confirm step); CatLookStore.Changed carries it to the stage and to the player's avatar.
    /// The elements come from the caller, their look from its USS (classes cat-custo__*).
    /// </summary>
    public class CatCustomizationPanel
    {
        private const float DragDegreesPerPixel = 0.45f;
        // Spin kept after a fast drag: the release speed decays exponentially (per second), stops below the threshold.
        private const float SpinDecayPerSecond = 3f;
        private const float SpinStopDegreesPerSecond = 4f;
        private const float SpinMaxDegreesPerSecond = 1080f;

        private readonly CatCosmeticsCatalog _catalog;
        private readonly CatPreviewStage _stage;
        private readonly VisualElement _catView;
        private readonly VisualElement _tabs;
        private readonly Label _current;
        private readonly VisualElement _cells;
        private readonly Action<VisualElement, int> _dressCell;

        private CatSlot _openSlot = CatLook.Slots[0];
        private CatLook _look;
        private bool _dragging;
        private float _lastDragX;
        private float _lastDragTime;
        private float _spin; // deg/s: measured while dragging, kept (decaying) after release

        /// <param name="dressCell">Optional: called on each vignette built, with its index in the grid.</param>
        public CatCustomizationPanel(CatCosmeticsCatalog catalog, CatPreviewStage stage, VisualElement catView,
            VisualElement tabs, Label current, VisualElement cells, Action<VisualElement, int> dressCell = null)
        {
            _catalog = catalog;
            _stage = stage;
            _catView = catView;
            _tabs = tabs;
            _current = current;
            _cells = cells;
            _dressCell = dressCell;

            _catView.RegisterCallback<PointerDownEvent>(OnCatPointerDown);
            _catView.RegisterCallback<PointerMoveEvent>(OnCatPointerMove);
            _catView.RegisterCallback<PointerUpEvent>(OnCatPointerUp);
        }

        public bool IsSpinning => _spin != 0f;

        /// <summary>Player-facing names of the parts.</summary>
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

        /// <summary>Shows the saved look on the stage and in the grid, the cat back on its three-quarter view.</summary>
        public void Open()
        {
            _look = _catalog.Sanitize(CatLookStore.Load());
            if (_stage != null)
            {
                _stage.SetLive(true);
                _stage.Apply(_look);
                _stage.ResetYaw();
                _catView.style.backgroundImage = Background.FromRenderTexture(_stage.Texture);
            }
            Rebuild();
        }

        /// <summary>The screen is gone: a drag or a spin does not outlive it.</summary>
        public void Close()
        {
            _dragging = false;
            _spin = 0f;
            if (_stage != null) _stage.ResetYaw();
        }

        /// <summary>Each frame while open: the thrown cat slows down.</summary>
        public void Tick()
        {
            if (_dragging || _spin == 0f || _stage == null) return;
            float dt = Time.unscaledDeltaTime;
            _stage.AddYaw(_spin * dt);
            _spin *= Mathf.Exp(-SpinDecayPerSecond * dt);
            if (Mathf.Abs(_spin) < SpinStopDegreesPerSecond) _spin = 0f;
        }

        /// <summary>Opens a part's tab.</summary>
        public void ShowSlot(CatSlot slot)
        {
            _openSlot = slot;
            Rebuild();
        }

        /// <summary>Picks an item: the cat wears it at once and the look is saved.</summary>
        public void Choose(CatSlot slot, byte id)
        {
            if (_catalog == null || !_catalog.Contains(slot, id))
            {
                Diagnostics.Logs.GameLog.Warn("ui", "ui.cosmetic.refused", "slot", slot, "id", (int)id,
                    "reason", _catalog == null ? "no-catalog" : "unknown-id");
                return;
            }
            Diagnostics.Logs.GameLog.Info("ui", "ui.cosmetic.pick", "slot", slot, "id", (int)id);
            _look = _look.With(slot, id);
            CatLookStore.Save(_look); // the stage and the avatar follow through CatLookStore.Changed
            Rebuild();
        }

        /// <summary>Sets the cat spinning as if thrown at <paramref name="degreesPerSecond"/> (autoplay demo).</summary>
        public void Throw(float degreesPerSecond)
        {
            _dragging = false;
            _spin = Mathf.Clamp(degreesPerSecond, -SpinMaxDegreesPerSecond, SpinMaxDegreesPerSecond);
        }

        private void Rebuild()
        {
            if (_catalog == null) return;

            _tabs.Clear();
            foreach (CatSlot slot in CatLook.Slots)
            {
                if (_catalog.ItemsOf(slot).Count == 0) continue;
                var tab = new Label(SlotLabel(slot));
                tab.AddToClassList("cat-custo__tab");
                tab.EnableInClassList("cat-custo__tab--on", slot == _openSlot);
                CatSlot captured = slot;
                tab.RegisterCallback<ClickEvent>(_ => ShowSlot(captured));
                _tabs.Add(tab);
            }

            CatCosmeticItem chosen = _catalog.Find(_openSlot, _look.Get(_openSlot));
            _current.text = chosen != null ? chosen.displayName : string.Empty;

            _cells.Clear();
            int index = 0;
            foreach (CatCosmeticItem item in _catalog.ItemsOf(_openSlot))
            {
                VisualElement cell = BuildCell(item, item == chosen);
                _cells.Add(cell);
                _dressCell?.Invoke(cell, index++);
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

        private void OnCatPointerDown(PointerDownEvent evt)
        {
            _dragging = true;
            _spin = 0f; // grabbing the cat stops it
            _lastDragX = evt.position.x;
            _lastDragTime = Time.unscaledTime;
            _catView.CapturePointer(evt.pointerId);
        }

        private void OnCatPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging || _stage == null) return;
            float degrees = -(evt.position.x - _lastDragX) * DragDegreesPerPixel;
            _stage.AddYaw(degrees);
            // Speed of the drag, smoothed over the last moves: what the cat keeps when released.
            float dt = Mathf.Max(Time.unscaledTime - _lastDragTime, 1f / 240f);
            _spin = Mathf.Clamp(Mathf.Lerp(_spin, degrees / dt, 0.5f), -SpinMaxDegreesPerSecond, SpinMaxDegreesPerSecond);
            _lastDragX = evt.position.x;
            _lastDragTime = Time.unscaledTime;
        }

        private void OnCatPointerUp(PointerUpEvent evt)
        {
            _dragging = false;
            // Held still before letting go: no throw.
            if (Time.unscaledTime - _lastDragTime > 0.08f) _spin = 0f;
            _catView.ReleasePointer(evt.pointerId);
        }
    }
}
