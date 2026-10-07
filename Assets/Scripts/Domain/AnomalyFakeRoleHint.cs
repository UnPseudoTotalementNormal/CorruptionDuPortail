using System;
using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// GD wording (Mage Occulte / Abyss passives): "les Anomalies connaissent autant de rôles factices que leur nombre
    /// dans la partie". At game start the anomalies learn that many fake characters (roles of the composition nobody
    /// plays): roles they can claim without being contradicted. The anomalies share one draw, so they know the same
    /// fakes. The adapter passes only the fakes worth a bluff (non-anomaly roles). Decides, never applies.
    /// </summary>
    public static class AnomalyFakeRoleHint
    {
        /// <summary>
        /// Returns (viewer, fake target) pairs: <paramref name="anomalySeats"/>.Count fakes drawn from
        /// <paramref name="fakeIds"/> (capped at what exists), each told to every anomaly. Empty if either list is.
        /// </summary>
        public static List<(ulong Viewer, ulong Target)> Pick(
            IReadOnlyList<ulong> anomalySeats, IReadOnlyList<ulong> fakeIds, IRandomProvider rng)
        {
            if (anomalySeats == null) throw new ArgumentNullException(nameof(anomalySeats));
            if (fakeIds == null) throw new ArgumentNullException(nameof(fakeIds));

            var result = new List<(ulong, ulong)>();
            List<int> picks = StolenPowerSelector.SelectDistinct(fakeIds.Count, anomalySeats.Count, rng);
            foreach (ulong viewer in anomalySeats)
            {
                foreach (int pick in picks)
                {
                    result.Add((viewer, fakeIds[pick]));
                }
            }
            return result;
        }
    }
}
