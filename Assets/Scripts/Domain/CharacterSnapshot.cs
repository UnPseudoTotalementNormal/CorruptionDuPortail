using System;
using Characters;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Immutable, engine-free value object capturing exactly the fields the 4 winning conditions read.
    /// Built from live server state by <c>GameSnapshotBuilder.FromLiveState</c> (Story 2.1) — never here.
    /// Value semantics (all 6 fields in <see cref="Equals(CharacterSnapshot)"/> / <see cref="GetHashCode"/>)
    /// are load-bearing: the Epic 2 differential keys on this equality, so a field omitted here would make
    /// the differential blind to a lying mapping (Story 1.4 mutation-sentinel foundation).
    /// </summary>
    public sealed class CharacterSnapshot : IEquatable<CharacterSnapshot>
    {
        public ulong OwnerClientId { get; }
        public bool IsFake { get; }
        public bool IsCorrupted { get; }
        public bool IsChained { get; }
        public FactionType FactionType { get; }

        /// <summary>
        /// From <c>POmniscience.hackedCharacterClientId</c> — a PLAIN ulong on a NetworkBehaviour, NOT a
        /// NetworkVariable. The builder MUST read it off the live POmniscience instance, never a serialized Role.
        /// </summary>
        public ulong HackedByOmniscienceTarget { get; }

        public CharacterSnapshot(
            ulong ownerClientId,
            bool isFake,
            bool isCorrupted,
            bool isChained,
            FactionType factionType,
            ulong hackedByOmniscienceTarget)
        {
            OwnerClientId = ownerClientId;
            IsFake = isFake;
            IsCorrupted = isCorrupted;
            IsChained = isChained;
            FactionType = factionType;
            HackedByOmniscienceTarget = hackedByOmniscienceTarget;
        }

        public bool Equals(CharacterSnapshot other)
        {
            if (other is null)
            {
                return false;
            }

            return OwnerClientId == other.OwnerClientId
                && IsFake == other.IsFake
                && IsCorrupted == other.IsCorrupted
                && IsChained == other.IsChained
                && FactionType == other.FactionType
                && HackedByOmniscienceTarget == other.HackedByOmniscienceTarget;
        }

        public override bool Equals(object obj) => Equals(obj as CharacterSnapshot);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(OwnerClientId);
            hash.Add(IsFake);
            hash.Add(IsCorrupted);
            hash.Add(IsChained);
            hash.Add(FactionType);
            hash.Add(HackedByOmniscienceTarget);
            return hash.ToHashCode();
        }

        public static bool operator ==(CharacterSnapshot left, CharacterSnapshot right) =>
            left is null ? right is null : left.Equals(right);

        public static bool operator !=(CharacterSnapshot left, CharacterSnapshot right) => !(left == right);
    }
}
