using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ModelTest.B.T3
{
    /// <summary>Minimal item data shown by the inventory screen.</summary>
    [Serializable]
    public class InventoryItem
    {
        public string displayName = "Objet";
        [TextArea(2, 5)] public string description = "";
        [Tooltip("Optional. Without a sprite the slot shows the first letter of the name.")]
        public Sprite icon;
        [Min(1)] public int quantity = 1;

        public InventoryItem() { }

        public InventoryItem(string displayName, string description, int quantity)
        {
            this.displayName = displayName;
            this.description = description;
            this.quantity = quantity;
        }
    }

    /// <summary>
    /// Drives InventoryScreen.uxml. Only does what USS cannot do on its own:
    /// 1. creates one slot per inventory entry (plus empty slots),
    /// 2. keeps track of the selected slot (class B_slot--selected) and fills the detail panel,
    /// 3. toggles B_inventory--narrow on the root under a width breakpoint (no media queries in USS).
    /// Every visual (sizes, hover/selection animation, layout switch) lives in InventoryScreen.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class InventoryScreen : MonoBehaviour
    {
        // Element names (UXML).
        const string RootName = "inventory";
        const string GridName = "inventory-grid";
        const string DetailName = "inventory-detail";
        const string DetailIconName = "detail-icon";
        const string DetailGlyphName = "detail-glyph";
        const string DetailTitleName = "detail-name";
        const string DetailQuantityName = "detail-quantity";
        const string DetailDescriptionName = "detail-description";

        // USS classes.
        const string NarrowClass = "B_inventory--narrow";
        const string SlotClass = "B_slot";
        const string SlotEmptyClass = "B_slot--empty";
        const string SlotSelectedClass = "B_slot--selected";
        const string SlotIconClass = "B_slot__icon";
        const string SlotGlyphClass = "B_slot__glyph";
        const string SlotQuantityClass = "B_slot__quantity";
        const string DetailEmptyClass = "B_detail--empty";

        [SerializeField] UIDocument m_Document;

        [Tooltip("Total number of slots. Raised automatically if there are more items.")]
        [SerializeField, Min(1)] int m_SlotCount = 30;

        [Tooltip("Under this root width (panel units), the detail panel goes below the grid.")]
        [SerializeField, Min(0f)] float m_NarrowBreakpoint = 760f;

        [SerializeField] List<InventoryItem> m_Items = new List<InventoryItem>
        {
            new InventoryItem("Épée rouillée", "Une lame ébréchée. Mieux que rien.", 1),
            new InventoryItem("Potion de soin", "Rend une partie des points de vie.", 5),
            new InventoryItem("Clé du portail", "Ouvre une porte scellée. Usage unique.", 1),
            new InventoryItem("Herbe amère", "Ingrédient d'alchimie courant.", 12),
            new InventoryItem("Parchemin", "Un texte à moitié effacé.", 2),
        };

        VisualElement m_Root;
        ScrollView m_Grid;
        VisualElement m_Detail;
        VisualElement m_DetailIcon;
        Label m_DetailGlyph;
        Label m_DetailTitle;
        Label m_DetailQuantity;
        Label m_DetailDescription;

        readonly List<VisualElement> m_Slots = new List<VisualElement>();
        int m_SelectedIndex = -1;

        /// <summary>Index of the selected slot, or -1.</summary>
        public int selectedIndex => m_SelectedIndex;

        /// <summary>Live list. Call <see cref="Rebuild"/> after modifying it.</summary>
        public List<InventoryItem> items => m_Items;

        void Reset()
        {
            m_Document = GetComponent<UIDocument>();
        }

        void OnEnable()
        {
            if (m_Document == null)
                m_Document = GetComponent<UIDocument>();

            var documentRoot = m_Document != null ? m_Document.rootVisualElement : null;
            if (documentRoot == null)
            {
                Debug.LogError($"[{nameof(InventoryScreen)}] No UIDocument root. Assign a UIDocument with InventoryScreen.uxml.", this);
                return;
            }

            m_Root = documentRoot.Q<VisualElement>(RootName);
            m_Grid = documentRoot.Q<ScrollView>(GridName);
            m_Detail = documentRoot.Q<VisualElement>(DetailName);
            m_DetailIcon = documentRoot.Q<VisualElement>(DetailIconName);
            m_DetailGlyph = documentRoot.Q<Label>(DetailGlyphName);
            m_DetailTitle = documentRoot.Q<Label>(DetailTitleName);
            m_DetailQuantity = documentRoot.Q<Label>(DetailQuantityName);
            m_DetailDescription = documentRoot.Q<Label>(DetailDescriptionName);

            if (m_Root == null || m_Grid == null || m_Detail == null || m_DetailIcon == null ||
                m_DetailGlyph == null || m_DetailTitle == null || m_DetailQuantity == null || m_DetailDescription == null)
            {
                Debug.LogError($"[{nameof(InventoryScreen)}] InventoryScreen.uxml elements not found (check element names).", this);
                m_Root = null;
                m_Grid = null;
                return;
            }

            m_Root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            Rebuild();
        }

        void OnDisable()
        {
            if (m_Root != null)
                m_Root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);

            m_Slots.Clear();
            m_Root = null;
            m_Grid = null;
            m_Detail = null;
        }

        /// <summary>Recreates every slot from <see cref="items"/>. Keeps the selection if still valid.</summary>
        public void Rebuild()
        {
            if (m_Grid == null)
                return;

            int previousSelection = m_SelectedIndex;
            m_SelectedIndex = -1;
            m_Slots.Clear();
            m_Grid.Clear(); // Clear/Add target the ScrollView content container.

            int count = Mathf.Max(m_SlotCount, m_Items.Count);
            for (int i = 0; i < count; i++)
            {
                var slot = CreateSlot(i, GetItem(i));
                m_Slots.Add(slot);
                m_Grid.Add(slot);
            }

            Select(previousSelection);
        }

        /// <summary>Selects a slot. An empty slot or an invalid index clears the selection.</summary>
        public void Select(int index)
        {
            var item = GetItem(index);
            if (item == null || index >= m_Slots.Count)
            {
                index = -1;
                item = null;
            }

            if (m_SelectedIndex >= 0 && m_SelectedIndex < m_Slots.Count)
                m_Slots[m_SelectedIndex].RemoveFromClassList(SlotSelectedClass);

            m_SelectedIndex = index;

            if (index >= 0)
                m_Slots[index].AddToClassList(SlotSelectedClass);

            UpdateDetail(item);
        }

        VisualElement CreateSlot(int index, InventoryItem item)
        {
            var slot = new VisualElement { focusable = true };
            slot.AddToClassList(SlotClass);

            if (item == null)
            {
                slot.AddToClassList(SlotEmptyClass);
            }
            else
            {
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList(SlotIconClass);
                var glyph = new Label { pickingMode = PickingMode.Ignore };
                glyph.AddToClassList(SlotGlyphClass);
                icon.Add(glyph);
                ApplyIcon(icon, glyph, item);
                slot.Add(icon);

                if (item.quantity > 1)
                {
                    var quantity = new Label(item.quantity.ToString()) { pickingMode = PickingMode.Ignore };
                    quantity.AddToClassList(SlotQuantityClass);
                    slot.Add(quantity);
                }
            }

            slot.RegisterCallback<ClickEvent>(_ => Select(index));
            slot.RegisterCallback<NavigationSubmitEvent>(_ => Select(index));
            return slot;
        }

        void UpdateDetail(InventoryItem item)
        {
            if (m_Detail == null)
                return;

            bool hasItem = item != null;
            m_Detail.EnableInClassList(DetailEmptyClass, !hasItem);

            // When cleared, the previous content is kept so it can fade out (see .B_detail--empty in USS).
            if (!hasItem)
                return;

            m_DetailTitle.text = item.displayName;
            m_DetailQuantity.text = $"Quantité : {item.quantity}";
            m_DetailDescription.text = item.description;
            ApplyIcon(m_DetailIcon, m_DetailGlyph, item);
        }

        void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            // Changing the class only affects the children layout; the root rect stays the same,
            // so this does not re-trigger in a loop.
            m_Root.EnableInClassList(NarrowClass, evt.newRect.width < m_NarrowBreakpoint);
        }

        InventoryItem GetItem(int index)
        {
            return index >= 0 && index < m_Items.Count ? m_Items[index] : null;
        }

        static void ApplyIcon(VisualElement icon, Label glyph, InventoryItem item)
        {
            if (item.icon != null)
            {
                icon.style.backgroundImage = new StyleBackground(item.icon);
                glyph.text = string.Empty;
            }
            else
            {
                icon.style.backgroundImage = StyleKeyword.Null;
                glyph.text = string.IsNullOrEmpty(item.displayName)
                    ? "?"
                    : item.displayName.Substring(0, 1).ToUpperInvariant();
            }
        }
    }
}
