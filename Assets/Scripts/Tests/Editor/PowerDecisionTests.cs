using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Powers v2 — EditMode characterization of pure power DECISIONS. No host, no NGO: build a
    /// PowerContext by hand, call Decide, assert the ordered effect list by value. This is the
    /// unit-test surface the v2 architecture exists to enable.
    /// </summary>
    [Category("PowerDecision")]
    public class PowerDecisionTests
    {
        [Test]
        public void CorruptionParanoia_RevealsOwnCorruptionToSelf_Broadcast()
        {
            var outcome = new CorruptionParanoiaDecision().Decide(new PowerContext(ownerSlot: 3));

            Assert.IsTrue(outcome.Accepted);
            Assert.AreEqual(1, outcome.UsesConsumed);
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new RevealInfo(3, RevealField.CorruptRevealed, RevealVisibility.Personal, 3, true),
            }, outcome.Effects);
        }

        [Test]
        public void CorruptionParanoia_IsPassive()
        {
            Assert.IsTrue(new CorruptionParanoiaDecision().IsPassive);
            Assert.AreEqual(PowerId.CorruptionParanoia, new CorruptionParanoiaDecision().Id);
        }

        // ---- Roster-reading passives -----------------------------------------------------
        private sealed class FakeRoster : IRosterView
        {
            public System.Collections.Generic.IReadOnlyList<int> Slots { get; set; } = new int[0];
            public readonly System.Collections.Generic.Dictionary<int, Characters.FactionType> Factions = new();
            public readonly System.Collections.Generic.Dictionary<int, string> Pseudos = new();
            public readonly System.Collections.Generic.Dictionary<int, int> Roles = new();
            public Characters.FactionType FactionOf(int slot) => Factions.TryGetValue(slot, out var f) ? f : default;
            public string PseudoOf(int slot) => Pseudos.TryGetValue(slot, out var p) ? p : "";
            public bool SameRole(int a, int b) => Roles.TryGetValue(a, out var ra) && Roles.TryGetValue(b, out var rb) && ra == rb;
        }

        [Test]
        public void CorruptionInsight_RevealsEveryCharacterCorruptionToOwner_InOrder()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 5 } };
            var outcome = new CorruptionInsightDecision().Decide(new PowerContext(ownerSlot: 0, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new RevealInfo(0, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, true),
                new RevealInfo(1, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, true),
                new RevealInfo(5, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, true),
            }, outcome.Effects);
        }

        [Test]
        public void CorruptionKnowledge_RevealsForceCorruptPerCharacter()
        {
            var roster = new FakeRoster { Slots = new[] { 2, 7 } };
            var outcome = new CorruptionKnowledgeDecision().Decide(new PowerContext(ownerSlot: 2, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new RevealInfo(2, RevealField.ForceCorruptOnRoleRevealed, RevealVisibility.Personal, 2, true),
                new RevealInfo(7, RevealField.ForceCorruptOnRoleRevealed, RevealVisibility.Personal, 2, true),
            }, outcome.Effects);
        }

        [Test]
        public void EyeOfTheVoid_DiscoversAnomalyChat_AnomaliesOnly()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Factions[0] = Characters.FactionType.chosen;
            roster.Factions[1] = Characters.FactionType.anomaly;
            roster.Factions[2] = Characters.FactionType.anomaly;

            var outcome = new EyeOfTheVoidDecision { AnomalyChatId = 1 }.Decide(new PowerContext(ownerSlot: 0, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new DiscoverChat(1, "", PowerEffectAudience.Specific(1)),
                new DiscoverChat(1, "", PowerEffectAudience.Specific(2)),
            }, outcome.Effects);
        }

        [Test]
        public void EyeOfTheVoid_NoAnomalies_AcceptsEmpty()
        {
            var roster = new FakeRoster { Slots = new[] { 0 } };
            roster.Factions[0] = Characters.FactionType.chosen;
            var outcome = new EyeOfTheVoidDecision { AnomalyChatId = 1 }.Decide(new PowerContext(ownerSlot: 0, roster: roster));
            Assert.IsTrue(outcome.Accepted);
            Assert.AreEqual(0, outcome.Effects.Count);
        }

        [Test]
        public void AutoCorruption_CorruptsOwner()
        {
            var outcome = new AutoCorruptionDecision().Decide(new PowerContext(ownerSlot: 4));
            CollectionAssert.AreEqual(new EffectDescriptor[] { new CorruptPlayer(4) }, outcome.Effects);
        }

        // ---- Active powers ---------------------------------------------------------------
        [Test]
        public void ChainedByShadows_RoleMatchChosen_TargetsRevealsChains()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Roles[1] = 7; roster.Roles[2] = 7;                 // target(1) same role as picked-role owner(2)
            roster.Factions[1] = Characters.FactionType.chosen;
            var ctx = new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster);

            var outcome = new ChainedByShadowsDecision().Decide(ctx);

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new AddToChain(1),
            }, outcome.Effects);
        }

        [Test]
        public void ChainedByShadows_RoleMatchNotChosen_NoChain()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Roles[1] = 7; roster.Roles[2] = 7;
            roster.Factions[1] = Characters.FactionType.anomaly;
            var outcome = new ChainedByShadowsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
            }, outcome.Effects);
        }

        [Test]
        public void ChainedByShadows_RoleMismatch_OnlyTargets()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            roster.Roles[1] = 7; roster.Roles[2] = 9;                 // different roles
            var outcome = new ChainedByShadowsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster));

            CollectionAssert.AreEqual(new EffectDescriptor[] { new NewTargeting(0, 1) }, outcome.Effects);
        }
    }
}
