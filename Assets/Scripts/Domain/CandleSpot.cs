using System;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>A rectangle on the table, seen from above (world x, z).</summary>
    public readonly struct TableRect
    {
        public readonly float MinX;
        public readonly float MinZ;
        public readonly float MaxX;
        public readonly float MaxZ;

        public TableRect(float minX, float minZ, float maxX, float maxZ)
        {
            MinX = Math.Min(minX, maxX);
            MinZ = Math.Min(minZ, maxZ);
            MaxX = Math.Max(minX, maxX);
            MaxZ = Math.Max(minZ, maxZ);
        }

        /// <summary>True if a disc of <paramref name="radius"/> centred on (x, z) touches this rectangle.</summary>
        public bool TouchesDisc(float x, float z, float radius)
        {
            float dx = Math.Max(MinX - x, Math.Max(0f, x - MaxX));
            float dz = Math.Max(MinZ - z, Math.Max(0f, z - MaxZ));
            return dx * dx + dz * dz < radius * radius;
        }
    }

    /// <summary>Where the vote candle stands and how big it is.</summary>
    public readonly struct CandleSpotResult
    {
        public readonly float X;
        public readonly float Z;
        public readonly float Scale;
        public readonly bool Free;

        public CandleSpotResult(float x, float z, float scale, bool free)
        {
            X = x;
            Z = z;
            Scale = scale;
            Free = free;
        }
    }

    /// <summary>
    /// Finds a free place on the table for the vote candle (board task T19): it must never cover a card nor its vote
    /// panel. It keeps the place chosen on the maquettes (compte-a-rebours v4: near the middle, right of the player's
    /// cards) when it is free, else takes the nearest free place around it (stepping back toward the player costs less
    /// than going sideways, going away more), on the player's side of the table (<c>maxZ</c>) and within view
    /// (<c>maxAbsX</c>); then the same smaller. A full table (11 seats and more, the near line as wide as the table):
    /// smaller still, wherever it fits on the player's side (the strip along the edge, the corner). With no room at all
    /// it stands, smallest, at the near corner on its side. Pure: world x, z on the table plane.
    /// </summary>
    public static class CandleSpot
    {
        private const float Step = 0.2f;
        private const float Reach = 12f;
        private static readonly float[] Scales = { 1f, 0.8f, 0.65f };
        // A full table (maquettes v4: « bord droit, plus petite »): smaller, anywhere on the player's side, out of view
        // at rest if it must (the strip along the table's edge, the corner).
        private static readonly float[] FullTableScales = { 0.65f, 0.5f };

        /// <param name="preferredX">The candle's own place (x).</param>
        /// <param name="preferredZ">The candle's own place (z).</param>
        /// <param name="radius">Radius of the candle's dish at scale 1, with its margin.</param>
        /// <param name="table">The table top.</param>
        /// <param name="cards">What must stay uncovered: each card with its vote panel.</param>
        /// <param name="maxAbsX">How far from the table's middle it may go and still be seen from the seat.</param>
        /// <param name="maxZ">How far from the player (z) it may go: it stays on the player's side of the table.</param>
        public static CandleSpotResult Find(float preferredX, float preferredZ, float radius, TableRect table,
            IReadOnlyList<TableRect> cards, float maxAbsX = float.MaxValue, float maxZ = float.MaxValue)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                if (TryFind(pass == 0 ? Scales : FullTableScales, pass == 0 ? maxAbsX : float.MaxValue, maxZ,
                        preferredX, preferredZ, radius, table, cards, out CandleSpotResult spot))
                {
                    return spot;
                }
            }
            // The near corner on its side, smallest: where a full table (14 seats) still leaves room. Free only if it
            // really is.
            float smallest = radius * FullTableScales[FullTableScales.Length - 1];
            float cornerX = preferredX < 0f ? table.MinX + smallest : table.MaxX - smallest;
            float cornerZ = table.MinZ + smallest;
            return new CandleSpotResult(cornerX, cornerZ, FullTableScales[FullTableScales.Length - 1], IsFree(cornerX, cornerZ, smallest, cards));
        }

        private static bool TryFind(float[] scales, float maxAbsX, float maxZ, float preferredX, float preferredZ,
            float radius, TableRect table, IReadOnlyList<TableRect> cards, out CandleSpotResult spot)
        {
            spot = default;
            foreach (float scale in scales)
            {
                float r = radius * scale;
                float minX = Math.Max(table.MinX + r, -maxAbsX), maxX = Math.Min(table.MaxX - r, maxAbsX);
                float minZ = table.MinZ + r, topZ = Math.Min(table.MaxZ - r, maxZOfCentre(maxZ, r));
                if (minX > maxX || minZ > topZ)
                {
                    continue;
                }
                float px = Math.Max(minX, Math.Min(maxX, preferredX));
                float pz = Math.Max(minZ, Math.Min(topZ, preferredZ));
                bool found = false;
                float bestX = 0f, bestZ = 0f, bestCost = float.MaxValue;
                int n = (int)Math.Round(Reach / Step);
                for (int i = -n; i <= n; i++)
                {
                    float x = px + i * Step;
                    if (x < minX - 1e-4f || x > maxX + 1e-4f)
                    {
                        continue;
                    }
                    for (int j = -n; j <= n; j++)
                    {
                        float z = pz + j * Step;
                        if (z < minZ - 1e-4f || z > topZ + 1e-4f)
                        {
                            continue;
                        }
                        float dx = x - px, dz = z - pz;
                        float cost = dx * dx + (dz < 0f ? 0.5f : 2f) * dz * dz;
                        if (cost < bestCost && IsFree(x, z, r, cards))
                        {
                            found = true;
                            bestCost = cost;
                            bestX = x;
                            bestZ = z;
                        }
                    }
                }
                if (found)
                {
                    spot = new CandleSpotResult(bestX, bestZ, scale, true);
                    return true;
                }
            }
            return false;
        }

        private static float maxZOfCentre(float maxZ, float r) => maxZ == float.MaxValue ? float.MaxValue : maxZ - r;

        private static bool IsFree(float x, float z, float r, IReadOnlyList<TableRect> cards)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].TouchesDisc(x, z, r))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
