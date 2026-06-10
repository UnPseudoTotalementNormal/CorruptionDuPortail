using System;
using System.Collections.Generic;
using System.Linq;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Immutable, engine-free snapshot of the whole game at decision time. Passed by argument to evaluators,
    /// never stored (so per-condition migrations stay independent). The character list is defensively copied
    /// and exposed read-only, so a caller mutating the source collection cannot mutate the snapshot.
    /// Equality is order-sensitive over <see cref="Characters"/> plus <see cref="Day"/> and
    /// <see cref="CurrentStateIndex"/>.
    /// </summary>
    public sealed class GameSnapshot : IEquatable<GameSnapshot>
    {
        public IReadOnlyList<CharacterSnapshot> Characters { get; }
        public int Day { get; }
        public int CurrentStateIndex { get; }

        public GameSnapshot(IEnumerable<CharacterSnapshot> characters, int day, int currentStateIndex)
        {
            if (characters == null)
            {
                throw new ArgumentNullException(nameof(characters));
            }

            // Defensive copy + read-only wrapper → immune to later mutation of the source collection,
            // and the exposed list cannot be cast back to a writable collection (throws on mutation).
            Characters = new List<CharacterSnapshot>(characters).AsReadOnly();
            Day = day;
            CurrentStateIndex = currentStateIndex;
        }

        public bool Equals(GameSnapshot other)
        {
            if (other is null)
            {
                return false;
            }

            return Day == other.Day
                && CurrentStateIndex == other.CurrentStateIndex
                && Characters.SequenceEqual(other.Characters);
        }

        public override bool Equals(object obj) => Equals(obj as GameSnapshot);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Day);
            hash.Add(CurrentStateIndex);
            foreach (var character in Characters)
            {
                hash.Add(character);
            }
            return hash.ToHashCode();
        }

        public static bool operator ==(GameSnapshot left, GameSnapshot right) =>
            left is null ? right is null : left.Equals(right);

        public static bool operator !=(GameSnapshot left, GameSnapshot right) => !(left == right);
    }
}
