using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Rejoin (feat/player-rejoin, step 1): server-side seats kept for real players who disconnected mid-game. A
    /// reserved seat is not chained: the player shows as left, his turns are skipped (he sleeps, he does not vote), and
    /// he may come back within the grace delay to take it back. Once the delay expires the seat falls back to the
    /// mid-game leave rule (instant chain + victory re-check). Pure and clock-injected (callers pass realtime seconds).
    /// </summary>
    public sealed class SeatReservations
    {
        private readonly Dictionary<ulong, double> _expiresAt = new();

        public int Count => _expiresAt.Count;

        public IEnumerable<ulong> ReservedIds => _expiresAt.Keys;

        /// <summary>Reserves the seat until <paramref name="now"/> + <paramref name="graceSeconds"/>. False when the
        /// seat is already reserved (the first reservation's deadline is kept).</summary>
        public bool Reserve(ulong clientId, double now, double graceSeconds)
        {
            if (_expiresAt.ContainsKey(clientId))
            {
                return false;
            }
            _expiresAt[clientId] = now + graceSeconds;
            return true;
        }

        public bool IsReserved(ulong clientId) => _expiresAt.ContainsKey(clientId);

        /// <summary>The player took his seat back (or the seat was settled): forget it. False when not reserved.</summary>
        public bool Release(ulong clientId) => _expiresAt.Remove(clientId);

        /// <summary>Seats whose grace delay is over at <paramref name="now"/>, removed from the reservations: the caller
        /// applies the leave rule to each, exactly once.</summary>
        public List<ulong> TakeExpired(double now)
        {
            var _expired = new List<ulong>();
            foreach (var _pair in _expiresAt)
            {
                if (now >= _pair.Value)
                {
                    _expired.Add(_pair.Key);
                }
            }
            foreach (ulong _id in _expired)
            {
                _expiresAt.Remove(_id);
            }
            _expired.Sort();
            return _expired;
        }

        public void Clear() => _expiresAt.Clear();
    }
}
