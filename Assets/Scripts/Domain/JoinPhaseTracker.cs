using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// NET-05 (epic-network-sync-hardening): server-side bookkeeping of clients that were APPROVED but have not yet
    /// finished synchronizing (GameScene load + spawns, up to 90 s on a slow PC). The lobby auto-start used to look at
    /// spawned Characters only, so a still-loading joiner did not block the start and then arrived in a started game
    /// as a character-less ghost. While this tracker holds anyone, the game must not start; a loader stuck past the
    /// sync cap is reported for disconnection so the lobby is never blocked forever.
    /// Pure and clock-injected (callers pass realtime seconds).
    /// </summary>
    public sealed class JoinPhaseTracker
    {
        private readonly Dictionary<ulong, double> _approvedAt = new();

        public bool HasSynchronizingClients => _approvedAt.Count > 0;

        public int SynchronizingCount => _approvedAt.Count;

        public void Approved(ulong clientId, double now) => _approvedAt[clientId] = now;

        public void Synchronized(ulong clientId) => _approvedAt.Remove(clientId);

        public void Left(ulong clientId) => _approvedAt.Remove(clientId);

        public void Clear() => _approvedAt.Clear();

        /// <summary>Clients approved more than <paramref name="limitSeconds"/> ago that are still synchronizing.</summary>
        public List<ulong> Expired(double now, double limitSeconds)
        {
            var _expired = new List<ulong>();
            foreach (var _pair in _approvedAt)
            {
                if (now - _pair.Value > limitSeconds)
                {
                    _expired.Add(_pair.Key);
                }
            }
            return _expired;
        }
    }
}
