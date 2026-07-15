using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// Pure selection math for the embodied emote wheel — the single decision point that maps a 2D pointing
    /// direction to the highlighted emote sector, so the wheel feel is EditMode-testable with ZERO scene / UI /
    /// input setup (mirrors <see cref="EmbodiedLookClamp"/>: a stateless float/vector function in the Game
    /// asmdef, not Domain — it uses <see cref="Mathf"/>/<see cref="Vector2"/>).
    ///
    /// CONVENTION: angles are measured CLOCKWISE from UP (screen-up = 0°), matching a radial menu laid out with
    /// the first emote at the top and the rest going clockwise. Emote <c>i</c> is CENTERED at
    /// <see cref="SectorCenterAngle"/> = <c>i * 360 / count</c>; its wedge is that center ± half a sector.
    ///
    /// The pointing direction comes from EITHER an accumulated mouse delta (cursor locked — a virtual stick) OR
    /// the pointer offset from the wheel centre (cursor free — hover). Both are the same math space here
    /// (+X = right, +Y = up); the caller converts screen coordinates before calling. A direction shorter than
    /// <paramref name="deadzone"/> means "no emote pointed at" → index <c>-1</c> (release plays nothing).
    /// </summary>
    public static class EmoteWheelSelection
    {
        public const int None = -1;

        /// <summary>
        /// Angle of <paramref name="direction"/> in degrees, CLOCKWISE from UP, normalized to [0, 360).
        /// (0,+1)=0°, (+1,0)=90°, (0,-1)=180°, (-1,0)=270°. Zero vector returns 0°.
        /// </summary>
        public static float AngleClockwiseFromUp(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0f)
            {
                return 0f;
            }
            // atan2(x, y): swapping the usual (y, x) rotates the zero to UP and flips to clockwise.
            float _deg = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
            return Mathf.Repeat(_deg, 360f);
        }

        /// <summary>Centre angle (deg, CW from up) of sector <paramref name="index"/> — the UI icon anchor.</summary>
        public static float SectorCenterAngle(int index, int count)
        {
            if (count <= 0)
            {
                return 0f;
            }
            return Mathf.Repeat(index, count) * (360f / count);
        }

        /// <summary>
        /// The emote index the direction points at, or <see cref="None"/> when the pointer is inside the
        /// dead-zone or there are no emotes. Sectors are equal wedges centred on each emote; the nearest centre
        /// wins (round-to-nearest with wrap), so a direction anywhere in emote <c>i</c>'s wedge returns <c>i</c>.
        /// </summary>
        public static int SelectionIndex(Vector2 direction, int count, float deadzone)
        {
            if (count <= 0)
            {
                return None;
            }
            if (direction.magnitude < deadzone)
            {
                return None;
            }

            float _angle = AngleClockwiseFromUp(direction);
            float _sector = 360f / count;
            // Round to the nearest sector centre, then wrap (angle just shy of 360° rounds up to count → 0).
            int _index = Mathf.RoundToInt(_angle / _sector);
            return ((_index % count) + count) % count;
        }
    }
}
