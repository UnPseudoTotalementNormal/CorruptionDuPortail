using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// NET-11 (epic-network-sync-hardening): the SERVER's record of who belongs to which chat channel. Membership
    /// used to live only on each client (filled by discovery RPCs) while every message was broadcast to everyone and
    /// filtered locally, so whether a message was kept depended on whether it arrived before or after that client's
    /// discovery. The server now decides membership and routes a private message only to the members it knows at the
    /// moment it processes the message. Public channels (general, server) are implicit: everyone is a member.
    /// </summary>
    public sealed class ChatMembership
    {
        private readonly HashSet<int> _publicChannels;
        private readonly Dictionary<int, HashSet<ulong>> _members = new();

        public ChatMembership(params int[] publicChannels)
        {
            _publicChannels = new HashSet<int>(publicChannels);
        }

        public bool IsPublic(int chatId) => _publicChannels.Contains(chatId);

        /// <summary>Adds the member. Returns true when it was not a member yet.</summary>
        public bool Grant(int chatId, ulong member)
        {
            if (IsPublic(chatId))
            {
                return false;
            }
            if (!_members.TryGetValue(chatId, out var _set))
            {
                _set = new HashSet<ulong>();
                _members[chatId] = _set;
            }
            return _set.Add(member);
        }

        /// <summary>Removes the member. Returns true when it was a member.</summary>
        public bool Revoke(int chatId, ulong member)
            => !IsPublic(chatId) && _members.TryGetValue(chatId, out var _set) && _set.Remove(member);

        public bool IsMember(int chatId, ulong member)
            => IsPublic(chatId) || (_members.TryGetValue(chatId, out var _set) && _set.Contains(member));

        /// <summary>Members of a private channel, in a stable order (empty for a public or unknown channel).</summary>
        public IReadOnlyList<ulong> MembersOf(int chatId)
        {
            var _result = new List<ulong>();
            if (!IsPublic(chatId) && _members.TryGetValue(chatId, out var _set))
            {
                _result.AddRange(_set);
                _result.Sort();
            }
            return _result;
        }

        public void Clear() => _members.Clear();
    }
}
