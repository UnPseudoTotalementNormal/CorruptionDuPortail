using UnityEngine;

namespace UI.CatPaw
{
    /// <summary>
    /// The paw images (Resources/CatPaw, Wouh's drawings cut out of their background) and the point of each one that
    /// clicks: the tip of the top-left toe, like the tip of an arrow. Placeholder art: replacing the PNGs keeps working
    /// as long as the toe stays at the same place.
    /// </summary>
    public static class PawArt
    {
        // Click point in the source drawings, as a fraction of the image (x from the left, y from the top).
        private static readonly Vector2[] Hotspots =
        {
            new(34f / 376f, 58f / 392f),   // Idle
            new(52f / 387f, 92f / 422f),   // Claws
            new(62f / 383f, 160f / 470f),  // Press
            new(34f / 374f, 58f / 390f),   // Refused
        };

        private static readonly string[] Names = { "idle", "griffes", "clic", "interdit" };

        public static string Name(PawState _state) => Names[(int)_state];

        /// <summary>Hotspot as a fraction of the image, y from the top.</summary>
        public static Vector2 Hotspot(PawState _state) => Hotspots[(int)_state];

        /// <summary>The paw's height on a 1080p screen (maquettes curseur-patte v1, T2).</summary>
        public const float HeightAt1080 = 48f;
    }
}
