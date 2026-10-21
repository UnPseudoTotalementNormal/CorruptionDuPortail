using UnityEngine;
using UnityEngine.UIElements;

namespace UI.LobbyRoles
{
    /// <summary>
    /// Paper pinned on cork, shared by the lobby tablet (<see cref="LobbyRolesUitkController"/>) and the players board
    /// (<see cref="LobbyPlayersBoard"/>). Mockup tools/maquettes/attribution-roles v9 A1: no two papers hang the same
    /// way. Each one gets a small tilt, a small shift and a pin place drawn from a stable key, so a rebuild never makes
    /// it jump; a clickable one straightens and lifts under the pointer (<see cref="Raise"/>).
    /// Sizes are panel pixels of the tablet's 1440 wide RenderTexture (the mockup's ±3 px at 1002 wide).
    /// </summary>
    public static class PinnedPaper
    {
        public const string PinClass = "lobby-roles__pin";
        public const string PinShineClass = "lobby-roles__pin-shine";
        public const string PinShadowClass = "lobby-roles__pin-shadow";

        internal const float MaxTiltDeg = 2.2f;
        internal const float MaxShiftPx = 4f;
        internal const float PinMinPercent = 30f;
        internal const float PinMaxPercent = 70f;
        internal const float LiftPx = 6f;
        internal const float LiftScale = 1.05f;

        private const int SaltTilt = 1;
        private const int SaltShiftX = 2;
        private const int SaltShiftY = 3;
        private const int SaltPin = 4;

        /// <summary>Where the pin of the paper <paramref name="key"/> goes, in percent of its width.</summary>
        public static float PinX(int key) => Mathf.Lerp(PinMinPercent, PinMaxPercent, Unit(key, SaltPin));

        internal static float TiltDeg(int key) => Mathf.Lerp(-MaxTiltDeg, MaxTiltDeg, Unit(key, SaltTilt));

        internal static Vector2 Shift(int key) => new(
            Mathf.Lerp(-MaxShiftPx, MaxShiftPx, Unit(key, SaltShiftX)),
            Mathf.Lerp(-MaxShiftPx, MaxShiftPx, Unit(key, SaltShiftY)));

        /// <summary>A red pushpin and its shadow stuck in the top edge, at x% of the width. Decoration only: never takes a click.</summary>
        public static void Pin(VisualElement on, float xPercent)
        {
            var shadow = new VisualElement { pickingMode = PickingMode.Ignore };
            shadow.AddToClassList(PinShadowClass);
            shadow.style.left = Length.Percent(xPercent);
            on.Add(shadow);

            var pin = new VisualElement { pickingMode = PickingMode.Ignore };
            pin.AddToClassList(PinClass);
            pin.style.left = Length.Percent(xPercent);
            var shine = new VisualElement { pickingMode = PickingMode.Ignore };
            shine.AddToClassList(PinShineClass);
            pin.Add(shine);
            on.Add(pin);
        }

        /// <summary>The paper as it hangs: its own tilt and shift.</summary>
        public static void Rest(VisualElement e, int key)
        {
            Vector2 shift = Shift(key);
            e.style.rotate = new Rotate(new Angle(TiltDeg(key)));
            e.style.translate = new Translate(new Length(shift.x), new Length(shift.y));
            e.style.scale = new Scale(Vector2.one);
        }

        /// <summary>The paper under the pointer: straight, lifted a little, slightly bigger.</summary>
        public static void Raise(VisualElement e, int key)
        {
            Vector2 shift = Shift(key);
            e.style.rotate = new Rotate(new Angle(0f));
            e.style.translate = new Translate(new Length(shift.x), new Length(shift.y - LiftPx));
            e.style.scale = new Scale(new Vector2(LiftScale, LiftScale));
        }

        // A fixed hash of (key, salt) into [0, 1): the same paper looks the same on every machine and every rebuild.
        internal static float Unit(int key, int salt)
        {
            unchecked
            {
                uint h = (uint)key * 0x9E3779B1u ^ (uint)salt * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }
    }
}
