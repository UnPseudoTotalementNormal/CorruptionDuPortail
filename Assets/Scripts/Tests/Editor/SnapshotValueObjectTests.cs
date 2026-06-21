using System;
using System.Collections.Generic;
using Characters;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Structural-invariant tests for the Domain snapshot value objects (Story 1.4).
    /// Proves value-by-value equality (every field in Equals/GetHashCode), immutability, and field round-trip —
    /// with NO live mapping. Test B is the anti-tautology mutation-sentinel FOUNDATION: it proves equality is
    /// sensitive to every single field, so the Epic 2 differential (which keys on snapshot equality) cannot be
    /// fooled by a lying mapping that corrupts one field.
    /// </summary>
    [Category("DomainSnapshot")]
    public class SnapshotValueObjectTests
    {
        private static CharacterSnapshot Reference() => new CharacterSnapshot(
            ownerClientId: 7,
            isFake: false,
            isCorrupted: true,
            isChained: false,
            factionType: FactionType.chosen,
            hackedByOmniscienceTarget: 42);

        // --- Test A: value equality + hash agreement ---

        [Test]
        public void CharacterSnapshot_IdenticalFields_AreEqual_AndShareHashCode()
        {
            var a = Reference();
            var b = Reference();

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a == b, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        // --- Test B: per-field inequality (mutation-sentinel foundation) ---

        private static readonly Func<CharacterSnapshot>[] OneFieldMutations =
        {
            () => new CharacterSnapshot(999, false, true, false, FactionType.chosen, 42),   // ownerClientId
            () => new CharacterSnapshot(7, true, true, false, FactionType.chosen, 42),       // isFake
            () => new CharacterSnapshot(7, false, false, false, FactionType.chosen, 42),     // isCorrupted
            () => new CharacterSnapshot(7, false, true, true, FactionType.chosen, 42),       // isChained
            () => new CharacterSnapshot(7, false, true, false, FactionType.anomaly, 42),     // factionType
            () => new CharacterSnapshot(7, false, true, false, FactionType.chosen, 999),     // hackedByOmniscienceTarget
        };

        [TestCaseSource(nameof(OneFieldMutations))]
        public void CharacterSnapshot_MutatingExactlyOneField_BreaksEquality(Func<CharacterSnapshot> mutated)
        {
            var reference = Reference();
            var corrupted = mutated();

            Assert.That(corrupted, Is.Not.EqualTo(reference),
                "Every field must participate in Equals — otherwise the Epic 2 differential is blind to a lying mapping on this field.");
        }

        // --- Test C: GameSnapshot value equality + order/scalar sensitivity ---

        [Test]
        public void GameSnapshot_SameContent_IsEqual()
        {
            var c1 = Reference();
            var c2 = new CharacterSnapshot(1, false, false, false, FactionType.anomaly, 0);

            var a = new GameSnapshot(new[] { c1, c2 }, day: 3, currentStateIndex: 5);
            var b = new GameSnapshot(new[] { c1, c2 }, day: 3, currentStateIndex: 5);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void GameSnapshot_ReorderedCharacters_IsNotEqual()
        {
            var c1 = Reference();
            var c2 = new CharacterSnapshot(1, false, false, false, FactionType.anomaly, 0);

            var a = new GameSnapshot(new[] { c1, c2 }, day: 3, currentStateIndex: 5);
            var reordered = new GameSnapshot(new[] { c2, c1 }, day: 3, currentStateIndex: 5);

            Assert.That(reordered, Is.Not.EqualTo(a), "Character order is significant (ChainingResolver is order-sensitive).");
        }

        [Test]
        public void GameSnapshot_DifferentDayOrStateIndex_IsNotEqual()
        {
            var c1 = Reference();

            var baseSnap = new GameSnapshot(new[] { c1 }, day: 3, currentStateIndex: 5);
            var otherDay = new GameSnapshot(new[] { c1 }, day: 4, currentStateIndex: 5);
            var otherIndex = new GameSnapshot(new[] { c1 }, day: 3, currentStateIndex: 6);

            Assert.That(otherDay, Is.Not.EqualTo(baseSnap));
            Assert.That(otherIndex, Is.Not.EqualTo(baseSnap));
        }

        // --- Test D: immutability ---

        [Test]
        public void GameSnapshot_MutatingSourceList_DoesNotAffectSnapshot()
        {
            var source = new List<CharacterSnapshot> { Reference() };
            var snapshot = new GameSnapshot(source, day: 1, currentStateIndex: 0);

            source.Add(new CharacterSnapshot(2, true, false, false, FactionType.unknown, 0));

            Assert.That(snapshot.Characters.Count, Is.EqualTo(1),
                "Snapshot must defensively copy its source collection.");
        }

        [Test]
        public void GameSnapshot_ExposedCharacters_IsNotWritable()
        {
            var snapshot = new GameSnapshot(new[] { Reference() }, day: 1, currentStateIndex: 0);

            Assert.That(snapshot.Characters, Is.AssignableTo<System.Collections.ObjectModel.ReadOnlyCollection<CharacterSnapshot>>());

            if (snapshot.Characters is IList<CharacterSnapshot> asList)
            {
                Assert.Throws<NotSupportedException>(() => asList.Add(Reference()),
                    "Exposed character list must reject mutation.");
            }
        }

        // --- Test E: field round-trip ---

        [Test]
        public void CharacterSnapshot_ConstructorArgs_RoundTripToProperties()
        {
            var s = new CharacterSnapshot(7, isFake: true, isCorrupted: false, isChained: true, FactionType.marginal, 123);

            Assert.That(s.OwnerClientId, Is.EqualTo(7UL));
            Assert.That(s.IsFake, Is.True);
            Assert.That(s.IsCorrupted, Is.False);
            Assert.That(s.IsChained, Is.True);
            Assert.That(s.FactionType, Is.EqualTo(FactionType.marginal));
            Assert.That(s.HackedByOmniscienceTarget, Is.EqualTo(123UL));
        }

        [Test]
        public void GameSnapshot_ConstructorArgs_RoundTripToProperties()
        {
            var c = Reference();
            var snapshot = new GameSnapshot(new[] { c }, day: 9, currentStateIndex: 4);

            Assert.That(snapshot.Day, Is.EqualTo(9));
            Assert.That(snapshot.CurrentStateIndex, Is.EqualTo(4));
            Assert.That(snapshot.Characters.Count, Is.EqualTo(1));
            Assert.That(snapshot.Characters[0], Is.EqualTo(c));
        }

        // --- Test F: null-argument guard (added coverage) ---

        [Test]
        public void GameSnapshot_NullCharacters_ThrowsArgumentNullException()
        {
            // GameSnapshot.cs line 22 throws ArgumentNullException(nameof(characters)) — pins the constructor guard.
            Assert.Throws<ArgumentNullException>(() => new GameSnapshot(null, day: 0, currentStateIndex: 0));
        }
    }
}
