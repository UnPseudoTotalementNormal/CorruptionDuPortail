using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ModelTest.A.T3
{
    /// <summary>
    /// Drives the UI Toolkit inventory screen defined by <c>InventoryScreen.uxml</c> /
    /// <c>InventoryScreen.uss</c>.
    ///
    /// The C# here is limited to what USS/UXML cannot express on their own:
    ///  - cloning one slot per data item from <c>InventorySlot.uxml</c>;
    ///  - handling clicks to drive the animated selection state (a USS class);
    ///  - reflowing to a stacked layout on narrow screens, because UI Toolkit has no
    ///    USS media queries (a <see cref="GeometryChangedEvent"/> toggles a class).
    ///
    /// Everything visual — the wrapping responsive grid, the hover state and all
    /// transitions — lives in USS.
    ///
    /// Requires a <see cref="UIDocument"/> whose Source Asset is
    /// <c>InventoryScreen.uxml</c>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class InventoryScreen : MonoBehaviour
    {
        /// <summary>One inventory entry. Plain serializable data — no assets required.</summary>
        [Serializable]
        public class Item
        {
            [Tooltip("Displayed name of the item.")]
            public string Name = "Item";

            [Tooltip("Stack size shown as a badge; values <= 1 hide the badge.")]
            public int Count = 1;

            [Tooltip("Optional icon. If empty, the slot shows a plain colored square.")]
            public Texture2D Icon;

            [TextArea(2, 6)]
            [Tooltip("Description shown in the detail panel.")]
            public string Description = "";
        }

        [Header("Data")]
        [SerializeField] private List<Item> _items = new List<Item>();

        [Header("Template")]
        [Tooltip("InventorySlot.uxml — one slot instance is cloned per item.")]
        [SerializeField] private VisualTreeAsset _slotTemplate;

        [Header("Responsive")]
        [Tooltip("Below this body width (px) the detail panel drops below the grid.")]
        [SerializeField] private float _narrowBreakpoint = 620f;

        // USS classes toggled from code.
        private const string NarrowClass = "A_inventory__body--narrow";
        private const string SelectedClass = "A_inventory-slot--selected";

        // Element names in the UXML.
        private const string BodyName = "body";
        private const string GridName = "grid";
        private const string DetailEmptyName = "detail-empty";
        private const string DetailContentName = "detail-content";
        private const string DetailIconName = "detail-icon";
        private const string DetailNameName = "detail-name";
        private const string DetailCountName = "detail-count";
        private const string DetailDescriptionName = "detail-description";

        // Named children inside a slot template.
        private const string SlotRootName = "slot";
        private const string SlotIconName = "slot-icon";
        private const string SlotCountName = "slot-count";
        private const string SlotNameName = "slot-name";

        private UIDocument _document;
        private VisualElement _body;
        private VisualElement _grid;
        private VisualElement _detailEmpty;
        private VisualElement _detailContent;
        private VisualElement _detailIcon;
        private Label _detailName;
        private Label _detailCount;
        private Label _detailDescription;

        private VisualElement _selectedSlot;
        private bool _isNarrow;

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root == null)
            {
                Debug.LogWarning($"{nameof(InventoryScreen)}: rootVisualElement is not ready; is a Source Asset assigned on the UIDocument?", this);
                return;
            }

            _body = root.Q<VisualElement>(BodyName);
            _grid = root.Q<VisualElement>(GridName);
            _detailEmpty = root.Q<VisualElement>(DetailEmptyName);
            _detailContent = root.Q<VisualElement>(DetailContentName);
            _detailIcon = root.Q<VisualElement>(DetailIconName);
            _detailName = root.Q<Label>(DetailNameName);
            _detailCount = root.Q<Label>(DetailCountName);
            _detailDescription = root.Q<Label>(DetailDescriptionName);

            if (_grid == null || _body == null)
            {
                Debug.LogWarning($"{nameof(InventoryScreen)}: expected elements '{BodyName}' / '{GridName}' not found in the UXML.", this);
                return;
            }

            BuildSlots();
            ShowEmptyDetail();

            // No USS media queries in UI Toolkit: reflow from geometry instead.
            _body.RegisterCallback<GeometryChangedEvent>(OnBodyGeometryChanged);
        }

        private void OnDisable()
        {
            _body?.UnregisterCallback<GeometryChangedEvent>(OnBodyGeometryChanged);
        }

        private void BuildSlots()
        {
            _grid.Clear();
            _selectedSlot = null;

            if (_slotTemplate == null)
            {
                Debug.LogWarning($"{nameof(InventoryScreen)}: no slot template assigned; assign InventorySlot.uxml.", this);
                return;
            }

            foreach (Item item in _items)
            {
                if (item == null)
                    continue;

                // CloneTree returns a TemplateContainer wrapping the slot; reparent the slot
                // itself into the grid so the wrapper does not disturb the wrapping layout.
                TemplateContainer clone = _slotTemplate.CloneTree();
                VisualElement slot = clone.Q<VisualElement>(SlotRootName);
                if (slot == null)
                {
                    Debug.LogWarning($"{nameof(InventoryScreen)}: slot template has no element named '{SlotRootName}'.", this);
                    return;
                }

                var icon = slot.Q<VisualElement>(SlotIconName);
                if (icon != null && item.Icon != null)
                    icon.style.backgroundImage = new StyleBackground(item.Icon);

                var count = slot.Q<Label>(SlotCountName);
                if (count != null)
                {
                    bool showBadge = item.Count > 1;
                    count.text = showBadge ? item.Count.ToString() : string.Empty;
                    count.style.display = showBadge ? DisplayStyle.Flex : DisplayStyle.None;
                }

                var nameLabel = slot.Q<Label>(SlotNameName);
                if (nameLabel != null)
                    nameLabel.text = item.Name;

                Item captured = item;
                VisualElement capturedSlot = slot;
                slot.RegisterCallback<ClickEvent>(_ => Select(capturedSlot, captured));

                _grid.Add(slot);
            }
        }

        private void Select(VisualElement slot, Item item)
        {
            if (_selectedSlot != null)
                _selectedSlot.RemoveFromClassList(SelectedClass);

            _selectedSlot = slot;
            _selectedSlot.AddToClassList(SelectedClass);

            ShowDetail(item);
        }

        private void ShowDetail(Item item)
        {
            if (_detailEmpty != null) _detailEmpty.style.display = DisplayStyle.None;
            if (_detailContent != null) _detailContent.style.display = DisplayStyle.Flex;

            if (_detailIcon != null)
            {
                if (item.Icon != null)
                    _detailIcon.style.backgroundImage = new StyleBackground(item.Icon);
                else
                    _detailIcon.style.backgroundImage = StyleKeyword.None;
            }

            if (_detailName != null) _detailName.text = item.Name;

            if (_detailCount != null)
            {
                bool hasStack = item.Count > 1;
                _detailCount.text = hasStack ? $"x{item.Count}" : string.Empty;
                _detailCount.style.display = hasStack ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_detailDescription != null) _detailDescription.text = item.Description;
        }

        private void ShowEmptyDetail()
        {
            if (_detailEmpty != null) _detailEmpty.style.display = DisplayStyle.Flex;
            if (_detailContent != null) _detailContent.style.display = DisplayStyle.None;
        }

        private void OnBodyGeometryChanged(GeometryChangedEvent evt)
        {
            float width = evt.newRect.width;
            if (width <= 0f)
                return;

            bool narrow = width < _narrowBreakpoint;
            if (narrow == _isNarrow)
                return;

            _isNarrow = narrow;
            _body.EnableInClassList(NarrowClass, narrow);
        }
    }
}
