using System;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// The circle the players sit in by day, once the table is gone (T19 + T23, Poyo 2026-10-21): the chained cat floats
    /// in the middle of the circle and takes a set share of the screen's height there, so the player sits that far from
    /// the middle. The others sit on a circle at least as big, grown with the number of players until every cat keeps
    /// enough of itself in sight from the player's eye (a cat is voted by clicking it) and no two cats touch.
    /// A cat is seen as an upright cylinder (its click shape); the player looks from its eye, on its own seat.
    /// Seat 0 is the player; seat k is k steps around the circle. Pure: no engine type, testable in EditMode.
    /// </summary>
    public static class DayRingLayout
    {
        /// <summary>Samples per side of a cat's silhouette when measuring how much of it is in sight.</summary>
        private const int Samples = 24;

        /// <summary>Steps (in metres) the circle grows by while it looks for room.</summary>
        private const float GrowthStep = 0.25f;

        /// <summary>
        /// How far from the middle the player sits: there a cat <paramref name="catHeight"/> high takes
        /// <paramref name="screenShare"/> of a screen of vertical field <paramref name="fovDegrees"/>.
        /// </summary>
        public static float PlayerDistance(float catHeight, float fovDegrees, float screenShare)
        {
            float span = 2f * (float)Math.Tan(fovDegrees * 0.5f * Math.PI / 180.0) * Math.Max(0.05f, screenShare);
            return span > 1e-4f ? catHeight / span : catHeight;
        }

        /// <summary>
        /// The others' circle for <paramref name="count"/> players: the smallest, from the player's distance up to
        /// <paramref name="maxGrowth"/> times it, where neighbours keep <paramref name="gap"/> between them and every cat
        /// keeps at least <paramref name="minVisibleShare"/> of its silhouette in sight.
        /// </summary>
        public static float OthersRadius(int count, float playerDistance, float catRadius, float catHeight, float eyeHeight,
            float gap, float minVisibleShare, float maxGrowth = 2f)
        {
            float max = playerDistance * Math.Max(1f, maxGrowth);
            for (float radius = playerDistance; radius < max; radius += GrowthStep)
            {
                if (Fits(count, playerDistance, radius, catRadius, catHeight, eyeHeight, gap, minVisibleShare))
                {
                    return radius;
                }
            }
            return max;
        }

        /// <summary>Whether neighbours keep their gap and every cat keeps its share in sight.</summary>
        public static bool Fits(int count, float playerDistance, float radius, float catRadius, float catHeight, float eyeHeight,
            float gap, float minVisibleShare)
        {
            if (count <= 2)
            {
                return true;
            }
            float chord = 2f * radius * (float)Math.Sin(Math.PI / count);
            return chord >= 2f * catRadius + gap
                   && LeastVisibleShare(count, playerDistance, radius, catRadius, catHeight, eyeHeight) >= minVisibleShare;
        }

        /// <summary>Share [0, 1] of the least seen cat's silhouette that no nearer cat hides, from the player's eye.</summary>
        public static float LeastVisibleShare(int count, float playerDistance, float radius, float catRadius, float catHeight,
            float eyeHeight)
        {
            if (count <= 2)
            {
                return 1f;
            }
            var boxes = new Box[count - 1];
            for (int seat = 1; seat < count; seat++)
            {
                boxes[seat - 1] = BoxOf(count, seat, playerDistance, radius, catRadius, catHeight, eyeHeight);
            }
            float least = 1f;
            foreach (Box box in boxes)
            {
                int seen = 0;
                for (int i = 0; i < Samples; i++)
                {
                    float yaw = box.YawMin + (i + 0.5f) / Samples * (box.YawMax - box.YawMin);
                    for (int j = 0; j < Samples; j++)
                    {
                        float pitch = box.PitchMin + (j + 0.5f) / Samples * (box.PitchMax - box.PitchMin);
                        if (!Hidden(boxes, box.Distance, yaw, pitch))
                        {
                            seen++;
                        }
                    }
                }
                least = Math.Min(least, seen / (float)(Samples * Samples));
            }
            return least;
        }

        /// <summary>Degrees the player turns from facing the middle to face seat <paramref name="seat"/> (+ = right).</summary>
        public static float SeatYaw(int count, int seat, float playerDistance, float radius)
        {
            Offset(count, seat, playerDistance, radius, out float x, out float z);
            return (float)(Math.Atan2(x, z) * 180.0 / Math.PI);
        }

        /// <summary>Metres from the player's seat to seat <paramref name="seat"/>.</summary>
        public static float SeatDistance(int count, int seat, float playerDistance, float radius)
        {
            Offset(count, seat, playerDistance, radius, out float x, out float z);
            return (float)Math.Sqrt(x * x + z * z);
        }

        /// <summary>The pitch (degrees, + = down) that looks from the eye at a point <paramref name="aimHeight"/> above the
        /// floor in the middle of the circle.</summary>
        public static float RestPitch(float eyeHeight, float aimHeight, float playerDistance)
        {
            return (float)(Math.Atan2(eyeHeight - aimHeight, Math.Max(1e-3f, playerDistance)) * 180.0 / Math.PI);
        }

        // Where seat k is from the player's seat: x to the right, z toward the middle.
        private static void Offset(int count, int seat, float playerDistance, float radius, out float x, out float z)
        {
            double angle = 2.0 * Math.PI * seat / Math.Max(1, count);
            x = (float)(radius * Math.Sin(angle));
            z = (float)(playerDistance - radius * Math.Cos(angle));
        }

        private static Box BoxOf(int count, int seat, float playerDistance, float radius, float catRadius, float catHeight,
            float eyeHeight)
        {
            Offset(count, seat, playerDistance, radius, out float x, out float z);
            float distance = (float)Math.Sqrt(x * x + z * z);
            float yaw = (float)Math.Atan2(x, z);
            float half = (float)Math.Asin(Math.Min(1f, catRadius / Math.Max(1e-3f, distance)));
            float front = Math.Max(1e-3f, distance - catRadius);
            return new Box
            {
                Distance = distance,
                YawMin = yaw - half,
                YawMax = yaw + half,
                PitchMin = (float)Math.Atan2(-eyeHeight, front),
                PitchMax = (float)Math.Atan2(catHeight - eyeHeight, front),
            };
        }

        private static bool Hidden(Box[] boxes, float distance, float yaw, float pitch)
        {
            foreach (Box other in boxes)
            {
                if (other.Distance < distance && yaw >= other.YawMin && yaw <= other.YawMax
                    && pitch >= other.PitchMin && pitch <= other.PitchMax)
                {
                    return true;
                }
            }
            return false;
        }

        // A cat's silhouette as seen from the eye: its yaw and pitch spans (radians), and how far it is.
        private struct Box
        {
            public float Distance;
            public float YawMin;
            public float YawMax;
            public float PitchMin;
            public float PitchMax;
        }
    }
}
