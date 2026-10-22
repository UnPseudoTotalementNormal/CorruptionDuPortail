using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// The chained cat's moment, seen seated by everyone (board task T19, maquettes enchainement v4: D2 + Y1). A force
    /// lifts the cat slowly while carrying it to the middle of the circle (easing in), it turns to face each player on
    /// that player's screen, its card leaves the row, comes in front of it and turns over; the card goes back, the cat
    /// falls to the floor, its flattest pose held half a second, it gets up, and under the day's black it is back on
    /// its seat. Every time is in seconds since the cat starts rising.
    /// The artist's clips: « Getting Chained » (0.708 s) then « Getting Chained 2 » (2.792 s): flattened at
    /// <see cref="ClipImpact"/>, flattest at <see cref="ClipFlat"/>; the part before the release is slowed down while
    /// the card is shown. Pure: no engine type, testable in EditMode.
    /// </summary>
    public static class ChainRevealTimeline
    {
        /// <summary>The cat reaches the middle of the circle, at its full height.</summary>
        public const float LiftEnd = 2f;

        /// <summary>Its card leaves the row, then arrives in front of it.</summary>
        public const float CardIn = 2.1f;
        public const float CardArrived = 2.7f;

        /// <summary>The card leaves the cat, then is back in its row.</summary>
        public const float CardOut = 4.35f;
        public const float CardBack = 4.8f;

        /// <summary>The force lets go: the cat falls.</summary>
        public const float Release = CardBack;

        /// <summary>It hits the floor (the clip's own squash).</summary>
        public const float Impact = Release + (ClipImpact - ClipRelease);

        /// <summary>The flattest pose, held <see cref="FreezeSeconds"/>.</summary>
        public const float Flat = Release + (ClipFlat - ClipRelease);
        public const float FreezeSeconds = 0.5f;

        /// <summary>The day's black comes: the cat goes back to its seat under it.</summary>
        public const float BlackOut = 7.2f;

        /// <summary>The room's darkness reaches its full depth this long after the start.</summary>
        public const float DarkenSeconds = 0.7f;

        // The artist's clips (seconds into « Getting Chained » then « Getting Chained 2 »).
        public const float FirstClipLength = 0.708f;
        public const float SecondClipLength = 2.792f;
        public const float ClipHold = 2.1f;
        public const float ClipRelease = 2.55f;
        public const float ClipImpact = 2.96f;
        public const float ClipFlat = 3.21f;
        public const float ClipEnd = FirstClipLength + SecondClipLength;

        /// <summary>The floating bob while the card is shown, as a share of the lift.</summary>
        public const float BobShare = 0.08f;

        /// <summary>
        /// Seconds into the chained clips at <paramref name="t"/>: played as is while the cat rises, slowed down while
        /// its card is shown, as is again from the release, stopped on the flattest pose for <see cref="FreezeSeconds"/>.
        /// Past <see cref="ClipEnd"/> the cat is in its chained idle.
        /// </summary>
        public static float ClipTime(float t)
        {
            if (t <= 0f)
            {
                return 0f;
            }
            if (t < LiftEnd)
            {
                return t * ClipHold / LiftEnd;
            }
            if (t < Release)
            {
                return ClipHold + (t - LiftEnd) / (Release - LiftEnd) * (ClipRelease - ClipHold);
            }
            if (t < Flat)
            {
                return ClipRelease + (t - Release);
            }
            if (t < Flat + FreezeSeconds)
            {
                return ClipFlat;
            }
            return ClipFlat + (t - Flat - FreezeSeconds);
        }

        /// <summary>Share [0, 1] of the way from its seat to the middle of the circle: slow at the end (ease out).</summary>
        public static float Travel(float t) => EaseOut(t / LiftEnd);

        /// <summary>
        /// Height above the floor as a share of the full lift: rises with <see cref="Travel"/>, floats a little while the
        /// card is shown, then falls (faster and faster) to 0 at <see cref="Impact"/>.
        /// </summary>
        public static float Height(float t)
        {
            if (t < Release)
            {
                float bob = t > LiftEnd ? BobShare * (float)Math.Sin((t - LiftEnd) * 2.4f) : 0f;
                return Travel(t) + bob;
            }
            if (t >= Impact)
            {
                return 0f;
            }
            float top = 1f + BobShare * (float)Math.Sin((Release - LiftEnd) * 2.4f);
            float fall = (t - Release) / (Impact - Release);
            return top * (1f - fall * fall);
        }

        /// <summary>Share [0, 1] of the way from the card's row slot to the front of the cat.</summary>
        public static float CardFlight(float t)
        {
            if (t <= CardIn || t >= CardBack)
            {
                return 0f;
            }
            if (t < CardArrived)
            {
                return EaseOut((t - CardIn) / (CardArrived - CardIn));
            }
            if (t < CardOut)
            {
                return 1f;
            }
            return 1f - SmoothStep((t - CardOut) / (CardBack - CardOut));
        }

        /// <summary>How dark the room is [0, 1]: falls over <see cref="DarkenSeconds"/>, until the black.</summary>
        public static float Darkness(float t) => t >= BlackOut ? 1f : SmoothStep(t / DarkenSeconds);

        /// <summary>
        /// How far from the player's eye the cat floats (mockups v4: D2, Poyo: D1 once the circle is big): the middle of
        /// the circle, but never nearer than the distance where a cat <paramref name="catHeight"/> high takes
        /// <paramref name="screenShare"/> of a screen of vertical field <paramref name="fovDegrees"/>.
        /// </summary>
        public static float HoverDistance(float ringDistance, float catHeight, float fovDegrees, float screenShare)
        {
            float span = 2f * (float)Math.Tan(fovDegrees * 0.5f * Math.PI / 180.0) * Math.Max(0.05f, screenShare);
            float fit = span > 1e-4f ? catHeight / span : ringDistance;
            return Math.Max(ringDistance, fit);
        }

        private static float EaseOut(float x)
        {
            x = Clamp01(x);
            float u = 1f - x;
            return 1f - u * u * u;
        }

        private static float SmoothStep(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
