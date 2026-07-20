using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode-pure coverage for the ONLY power-decision branches PowerDecisionTests does not already pin
    /// (verified against its method list — no duplicates). Assert on the returned EffectDescriptor contract.
    /// </summary>
    [Category("PowerDecision")]
    public class PowerDecisionUncoveredBranchTests
    {
        private sealed class FakeRoster : IRosterView
        {
            public IReadOnlyList<int> Slots { get; set; } = new int[0];
            public readonly Dictionary<int, Characters.FactionType> Factions = new();
            public readonly Dictionary<int, string> Pseudos = new();
            public readonly Dictionary<int, int> Roles = new();
            public readonly HashSet<int> Robots = new();
            public readonly HashSet<int> Healed = new();
            public readonly HashSet<int> Corrupted = new();
            public readonly Dictionary<int, string> RoleNames = new();
            public Characters.FactionType FactionOf(int slot) => Factions.TryGetValue(slot, out var f) ? f : default;
            public string PseudoOf(int slot) => Pseudos.TryGetValue(slot, out var p) ? p : "";
            public bool SameRole(int a, int b) => Roles.TryGetValue(a, out var ra) && Roles.TryGetValue(b, out var rb) && ra == rb;
            public bool IsRobot(int slot) => Robots.Contains(slot);
            public bool IsHealed(int slot) => Healed.Contains(slot);
            public bool IsCorrupted(int slot) => Corrupted.Contains(slot);
            public string RoleNameOf(int slot) => RoleNames.TryGetValue(slot, out var n) ? n : "";
        }

        // Blessing: PowerDecisionTests covers match/not-healed and match/already-healed, but NOT role-mismatch.
        [Test]
        public void Blessing_RoleMismatch_TargetsOnly()
        {
            var r = new FakeRoster();
            r.Roles[1] = 5; r.Roles[2] = 9;
            var o = new BlessingDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: r));
            Assert.IsTrue(o.Accepted);
            CollectionAssert.AreEqual(new EffectDescriptor[] { new NewTargeting(0, 1) }, o.Effects);
        }

        // EyeOfTheVoid: the existing test has a non-anomaly owner; this pins that an ANOMALY owner is itself
        // included in the discover fan-out (owner is not excluded).
        [Test]
        public void EyeOfTheVoid_OwnerIsAnomaly_OwnerAlsoDiscovers()
        {
            var r = new FakeRoster { Slots = new[] { 0, 1 } };
            r.Factions[0] = Characters.FactionType.anomaly;
            r.Factions[1] = Characters.FactionType.chosen;
            var o = new EyeOfTheVoidDecision { AnomalyChatId = 2 }.Decide(new PowerContext(ownerSlot: 0, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new DiscoverChat(2, "", PowerEffectAudience.Specific(0)),
            }, o.Effects);
        }

        // PersonalBeacons: existing covers robots-present; this pins the no-robot empty-accept branch.
        [Test]
        public void PersonalBeacons_NoRobot_AcceptsEmpty()
        {
            var r = new FakeRoster { Slots = new[] { 0, 1 } };
            var o = new PersonalBeaconsDecision().Decide(new PowerContext(ownerSlot: 0, roster: r));
            Assert.IsTrue(o.Accepted);
            Assert.AreEqual(0, o.Effects.Count);
        }

        // Omniscience: existing covers chosen/non-chosen with a roster; this pins the null-roster guard path
        // (Roster == null must be treated as non-chosen, no hack).
        [Test]
        public void Omniscience_NullRoster_TreatedAsNonChosen_NoHack()
        {
            var o = new OmniscienceDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                RequestCharacterRefresh.Instance,
            }, o.Effects);
        }

        // LackOfAffection: existing covers (chosen + true-local = both) and (non-chosen + not-local = empty).
        // These are the two UNcovered crossed combinations.
        [Test]
        public void LackOfAffection_ChosenTarget_NotTrueLocal_RevealsSenderRoleOnly()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.chosen;
            var o = new LackOfAffectionDecision().Decide(new PowerContext(ownerSlot: 5, targetSlot: 1, isTrueLocalTarget: false, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new RevealInfo(5, RevealField.RoleRevealed, RevealVisibility.Personal, 1, false),
            }, o.Effects);
        }

        [Test]
        public void LackOfAffection_NonChosenTarget_TrueLocal_ChatOnly()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.anomaly;
            r.RoleNames[5] = "Orpheline";
            var o = new LackOfAffectionDecision().Decide(new PowerContext(ownerSlot: 5, targetSlot: 1, isTrueLocalTarget: true, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new ChatLocal("Orpheline est venu(e) vous voir...", ChatWindows.Server),
            }, o.Effects);
        }

        // BoundByInk: existing supplies a real IInkChatState; this pins the missing-state fallback to id -1.
        [Test]
        public void BoundByInk_NoInkChatState_ChatIdFallsBackToMinusOne()
        {
            var o = new BoundByInkDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new DiscoverChat(-1, "Lié par l'encre", PowerEffectAudience.Specific(1)),
                new RegisterInkTarget(1),
            }, o.Effects);
        }
    }
}
