using UnityEngine;
using UnityEngine.UIElements;

namespace UI.Cards
{
    /// <summary>
    /// THE reusable UITK role-card face — one canonical component so a role card looks identical everywhere
    /// (lobby attribution grid, RoleCard detail, deduction board, …). A 5:7 portrait card (ratio 630/880 from
    /// Card.prefab): the portrait art fills the face (scale-and-crop), a dark scrim at the bottom carries the
    /// role name in gold, and a faction-tinted frame wraps it. Self-contained (inline styles) so it needs no
    /// per-screen USS wiring — create one and drop it anywhere:
    /// <code>root.Add(RoleCardElement.Create(name, portraitSprite, factionColor));</code>
    /// The card stays a PURE visual; screen-specific chrome (steppers, badges, dimming) is added around it by
    /// the caller, never baked in here.
    /// </summary>
    public sealed class RoleCardElement : VisualElement
    {
        public const float Ratio = 0.7159f; // width / height = 630 / 880 (Card.prefab)

        private static readonly Color FrameDefault = new Color(0.22f, 0.15f, 0.14f);
        private static readonly Color Fallback = new Color(0.10f, 0.05f, 0.055f);
        private static readonly Color Gold = new Color(0.913f, 0.772f, 0.478f);

        private readonly VisualElement _art;
        private readonly Label _name;
        private Color _accent = FrameDefault;

        public RoleCardElement()
        {
            style.flexShrink = 0;
            style.overflow = Overflow.Hidden;
            style.backgroundColor = Fallback;
            SetRadius(12f);
            SetBorder(2f, FrameDefault);

            _art = new VisualElement { name = "art", pickingMode = PickingMode.Ignore };
            Fill(_art);
            _art.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
            Add(_art);

            // Bottom scrim for name legibility (UITK has no gradients — a solid dark band does the job).
            var scrim = new VisualElement { pickingMode = PickingMode.Ignore };
            scrim.style.position = Position.Absolute;
            scrim.style.left = 0; scrim.style.right = 0; scrim.style.bottom = 0;
            scrim.style.height = Length.Percent(42f);
            scrim.style.backgroundColor = new Color(0.03f, 0.02f, 0.025f, 0.78f);
            Add(scrim);

            _name = new Label { pickingMode = PickingMode.Ignore };
            _name.style.position = Position.Absolute;
            _name.style.left = 0; _name.style.right = 0; _name.style.bottom = 0;
            _name.style.paddingLeft = 8; _name.style.paddingRight = 8;
            _name.style.paddingTop = 6; _name.style.paddingBottom = 10;
            _name.style.color = Gold;
            _name.style.unityFontStyleAndWeight = FontStyle.Bold;
            _name.style.unityTextAlign = TextAnchor.LowerCenter;
            _name.style.whiteSpace = WhiteSpace.Normal;
            _name.style.fontSize = 15;
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
        private void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
        }

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
