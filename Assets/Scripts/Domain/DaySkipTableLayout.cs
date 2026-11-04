using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// T27 (mockups bouton-skip v1 H3 + v2 S2): how high the day's skip table and its recap text stand. Both are worked
    /// out from the eye, so they stay at the same place on screen whatever the circle's size and the floor's depth. The
    /// slopes are the voted renders': a 3.6 m table seen from an eye 11.3 m up, 37.1 m away; the text 0.76 m above the eye.
    /// </summary>
    public static class DaySkipTableLayout
    {
        /// <summary>How far the table's top lies under the eye, per metre from the eye to the middle of the circle.</summary>
        public const float TopDropPerMetre = (11.3f - 3.6f) / 37.1f;
        /// <summary>How far the recap text lies above the eye, per metre from the eye to the middle of the circle.</summary>
        public const float TextRisePerMetre = 0.76f / 37.1f;
        /// <summary>The shortest table the turned shape can make (foot and top touch below that).</summary>
        public const float MinTableHeight = 1.2f;

        /// <summary>The table's height above the floor, for an eye <paramref name="eyeAboveFloor"/> above it.</summary>
        public static float TableHeight(float eyeAboveFloor, float eyeToMiddle)
        {
            return Math.Max(MinTableHeight, eyeAboveFloor - eyeToMiddle * TopDropPerMetre);
        }

        /// <summary>The recap text's height above the floor, for an eye <paramref name="eyeAboveFloor"/> above it.</summary>
        public static float RecapTextHeight(float eyeAboveFloor, float eyeToMiddle)
        {
            return eyeAboveFloor + eyeToMiddle * TextRisePerMetre;
        }
    }
}
