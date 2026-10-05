using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Rejoin 02 (feat/player-rejoin): server-side map between a player's SEAT (the NGO clientId he had when he was
    /// seated, still used as the key of his character, votes, chat, knowledge…) and the TRANSPORT clientId he is
    /// connected with now (NGO assigns a new one on every reconnect), plus the secret session token that proves a
    /// reconnecting client owns a seat. Ids without an alias map to themselves, so a player who never reconnected costs
    /// nothing. Pure; ids &gt;= 100 (simulated bots) never go through here.
    /// </summary>
    public sealed class SeatDirectory
    {
        private readonly Dictionary<string, ulong> _seatByToken = new();
        private readonly Dictionary<ulong, string> _tokenBySeat = new();
        private readonly Dictionary<ulong, ulong> _seatByTransport = new();
        private readonly Dictionary<ulong, ulong> _transportBySeat = new();

        /// <summary>Records <paramref name="token"/> as the proof of ownership of <paramref name="seat"/> (replaces any
        /// previous token of that seat).</summary>
        public void IssueToken(ulong seat, string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return;
            }
            if (_tokenBySeat.TryGetValue(seat, out string _previous))
            {
                _seatByToken.Remove(_previous);
            }
            _tokenBySeat[seat] = token;
            _seatByToken[token] = seat;
        }

        public bool TryGetSeatOfToken(string token, out ulong seat)
        {
            seat = 0;
            return !string.IsNullOrEmpty(token) && _seatByToken.TryGetValue(token, out seat);
        }

        public bool HasToken(ulong seat) => _tokenBySeat.ContainsKey(seat);

        /// <summary>From now on <paramref name="transport"/> plays <paramref name="seat"/>. Any older transport of that
        /// seat is unbound (one seat, one connection).</summary>
        public void Bind(ulong transport, ulong seat)
        {
            if (_transportBySeat.TryGetValue(seat, out ulong _old))
            {
                _seatByTransport.Remove(_old);
            }
            if (_seatByTransport.TryGetValue(transport, out ulong _oldSeat))
            {
                _transportBySeat.Remove(_oldSeat);
            }
            if (transport == seat)
            {
                // Back on its own id (e.g. after a host-side reset): no alias needed.
                _transportBySeat.Remove(seat);
                return;
            }
            _seatByTransport[transport] = seat;
            _transportBySeat[seat] = transport;
        }

        /// <summary>The transport is gone (disconnect): forget its alias. Returns the seat it played, if any.</summary>
        public bool Unbind(ulong transport, out ulong seat)
        {
            if (_seatByTransport.TryGetValue(transport, out seat))
            {
                _seatByTransport.Remove(transport);
                if (_transportBySeat.TryGetValue(seat, out ulong _current) && _current == transport)
                {
                    _transportBySeat.Remove(seat);
                }
                return true;
            }
            seat = transport;
            return false;
        }

        /// <summary>The seat played by a transport (itself when it never rejoined).</summary>
        public ulong SeatOf(ulong transport) => _seatByTransport.TryGetValue(transport, out ulong _seat) ? _seat : transport;

        /// <summary>The transport currently playing a seat (itself when it never rejoined).</summary>
        public ulong TransportOf(ulong seat) => _transportBySeat.TryGetValue(seat, out ulong _transport) ? _transport : seat;

        public bool IsAlias(ulong transport) => _seatByTransport.ContainsKey(transport);

        public void Clear()
        {
            _seatByToken.Clear();
            _tokenBySeat.Clear();
            _seatByTransport.Clear();
            _transportBySeat.Clear();
        }
    }
}
