using UnityEngine;
using UnityEngine.UIElements;

namespace UI.Cards
{
    /// <summary>
    /// THE reusable UITK role-card face — one canonical component so a role card looks identical everywhere
    /// (lobby attribution grid, RoleCard detail, deduction board, …). A 5:7 portrait card (ratio 630/880 from
    /// Card.prefab): the portrait is INSET in a near-black matte (scale-and-crop), the role name sits in gold in
    /// the bottom matte band (below the art, no overlay), and a faction-tinted frame wraps it. Self-contained so
    /// it needs no
    /// per-screen USS wiring — create one and drop it anywhere:
    /// <code>root.Add(RoleCardElement.Create(name, portraitSprite, factionColor));</code>
    /// The card stays a PURE visual; screen-specific chrome (steppers, badges, dimming) is added around it by
    /// the caller, never baked in here.
    /// </summary>
    public sealed class RoleCardElement : VisualElement
    {
        public const float Ratio = 0.7159f; // width / height = 630 / 880 (Card.prefab)

        private static readonly Color FrameDefault = new Color(0.22f, 0.15f, 0.14f);
        private static readonly Color Matte = new Color(0.031f, 0.023f, 0.027f);  // near-black card = the frame/matte
        private static readonly Color Fallback = new Color(0.10f, 0.05f, 0.055f); // deep warm art fallback
        private static readonly Color Gold = new Color(0.913f, 0.772f, 0.478f);

        private readonly VisualElement _art;
        private readonly Label _name;
        private Color _accent = FrameDefault;

        public RoleCardElement()
        {
            style.flexShrink = 0;
            style.flexDirection = FlexDirection.Column;
            style.overflow = Overflow.Hidden;
            style.backgroundColor = Matte;              // the black matte/frame the art is inset into
            style.paddingTop = 7; style.paddingLeft = 7; style.paddingRight = 7; style.paddingBottom = 6;
            SetRadius(10f);
            SetBorder(1.5f, FrameDefault);

            // Portrait fills the top of the matte (INSET, not edge-to-edge), scale-and-crop, slightly rounded.
            _art = new VisualElement { name = "art", pickingMode = PickingMode.Ignore };
            _art.style.flexGrow = 1;
            _art.style.flexShrink = 1;
            _art.style.minHeight = 0;
            _art.style.backgroundColor = Fallback;
            _art.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
            _art.style.borderTopLeftRadius = 5; _art.style.borderTopRightRadius = 5;
            _art.style.borderBottomLeftRadius = 5; _art.style.borderBottomRightRadius = 5;
            Add(_art);

            // Role name in the bottom matte band (BELOW the art, on the black card — no overlay scrim).
            _name = new Label { pickingMode = PickingMode.Ignore };
            _name.style.flexShrink = 0;
            _name.style.marginTop = 7;
            _name.style.color = Gold;
            _name.style.unityFontStyleAndWeight = FontStyle.Bold;
            _name.style.unityTextAlign = TextAnchor.MiddleCenter;
            _name.style.whiteSpace = WhiteSpace.Normal;
            _name.style.fontSize = 14;
            Add(_name);
        }

        /// <summary>Set an explicit width; the height is derived from the 5:7 ratio (deterministic — no async layout pass).</summary>
        public RoleCardElement SetWidth(float width)
        {
            style.width = width;
            style.height = width / Ratio;
            return this;
        }

        /// <summary>Set the portrait sprite (fills the card, scale-and-crop). Null clears to the deep fallback.</summary>
        public RoleCardElement SetPortrait(Sprite sprite)
        {
            _art.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : new StyleBackground();
            return this;
        }

        public RoleCardElement SetName(string roleName)
        {
            _name.text = roleName;
            return this;
        }

        /// <summary>Faction accent — tints the frame (kept dark so the portrait stays the hero).</summary>
        public RoleCardElement SetAccent(Color accent)
        {
            _accent = Color.Lerp(accent, Color.black, 0.45f); // darkened so the frame reads as a frame, not a glow
            SetBorder(2f, _accent);
            return this;
        }

        /// <summary>Convenience factory: name + portrait + faction accent in one call.</summary>
        public static RoleCardElement Create(string roleName, Sprite portrait, Color accent)
            => new RoleCardElement().SetName(roleName).SetPortrait(portrait).SetAccent(accent);

        // ---- style helpers ----
        private void SetRadius(float r)
        {
            style.borderTopLeftRadius = r; style.borderTopRightRadius = r;
            style.borderBottomLeftRadius = r; style.borderBottomRightRadius = r;
        }

        private void SetBorder(float w, Color c)
        {
            style.borderTopWidth = w; style.borderBottomWidth = w;
            style.borderLeftWidth = w; style.borderRightWidth = w;
            style.borderTopColor = c; style.borderBottomColor = c;
            style.borderLeftColor = c; style.borderRightColor = c;
        }
    }
}
