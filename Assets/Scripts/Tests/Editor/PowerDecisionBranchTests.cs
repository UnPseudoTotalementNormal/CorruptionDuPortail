using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode-pure branch coverage for the power decisions the catalog marked "à implémenter" (A1/A2): the
    /// CONDITIONAL branches PowerDecisionTests did not yet pin. Same pattern — build a PowerContext + FakeRoster,
    /// call Decide, assert the ordered EffectDescriptor list BY VALUE (assert on the contract, never the line).
    /// </summary>
    [Category("PowerDecision")]
    public class PowerDecisionBranchTests
    {
        private sealed class FakeRoster : IRosterView
        {
            public IReadOnlyList<int> Slots { get; set; } = new int[0];
            public readonly Dictionary<int, Characters.FactionType> Factions = new();
            public readonly Dictionary<int, string> Pseudos = new();
            public readonly Dictionary<int, int> Roles = new();
            public readonly HashSet<int> Robots = new();
            public readonly HashSet<int> Healed = new();
            public readonly Dictionary<int, string> RoleNames = new();
            public Characters.FactionType FactionOf(int slot) => Factions.TryGetValue(slot, out var f) ? f : default;
            public string PseudoOf(int slot) => Pseudos.TryGetValue(slot, out var p) ? p : "";
            public bool SameRole(int a, int b) => Roles.TryGetValue(a, out var ra) && Roles.TryGetValue(b, out var rb) && ra == rb;
            public bool IsRobot(int slot) => Robots.Contains(slot);
            public bool IsHealed(int slot) => Healed.Contains(slot);
            public string RoleNameOf(int slot) => RoleNames.TryGetValue(slot, out var n) ? n : "";
        }

        // ---- Blessing (BlessingDecision) --------------------------------------------------
        [Test]
        public void Blessing_RoleMismatch_TargetsOnly()
        {
            var r = new FakeRoster();
            r.Roles[1] = 5; r.Roles[2] = 9; // picked target and picked role differ
            var o = new BlessingDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: r));
            Assert.IsTrue(o.Accepted);
            CollectionAssert.AreEqual(new EffectDescriptor[] { new NewTargeting(0, 1) }, o.Effects);
        }

        [Test]
        public void Blessing_RoleMatch_AlreadyHealed_SkipsHeal_StillRevealsBlessesAnnounces()
        {
            var r = new FakeRoster();
            r.Roles[1] = 7; r.Roles[2] = 7; r.Healed.Add(1); r.Pseudos[1] = "Bob";
            var o = new BlessingDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new SetBlessed(1),
                new ChatBroadcast("Bob est maintenant béni.", ChatWindows.Server, PowerEffectAudience.Specific(0)),
            }, o.Effects);
        }

        // ---- EyeOfTheVoid (EyeOfTheVoidDecision) ------------------------------------------
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

        // ---- TruthChains (TruthChainsDecision) --------------------------------------------
        [Test]
        public void TruthChains_NonAnomalyTarget_TargetsOnly()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.chosen;
            var o = new TruthChainsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[] { new NewTargeting(0, 1) }, o.Effects);
        }

        [Test]
        public void TruthChains_AnomalyTarget_ChainsAndAnnounces()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.anomaly; r.Pseudos[1] = "Bob";
            var o = new TruthChainsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new AddToChain(1),
                new ChatSendServer("Bob sera lié par les chaînes de la vérité.", ChatWindows.Server),
            }, o.Effects);
        }

        // ---- PersonalBeacons (PersonalBeaconsDecision) ------------------------------------
        [Test]
        public void PersonalBeacons_NoRobot_AcceptsEmpty()
        {
            var r = new FakeRoster { Slots = new[] { 0, 1 } };
            var o = new PersonalBeaconsDecision().Decide(new PowerContext(ownerSlot: 0, roster: r));
            Assert.IsTrue(o.Accepted);
            Assert.AreEqual(0, o.Effects.Count);
        }

        [Test]
        public void PersonalBeacons_RobotsPresent_RevealsForceCorruptPerRobot()
        {
            var r = new FakeRoster { Slots = new[] { 0, 1, 2 } };
            r.Robots.Add(1); r.Robots.Add(2);
            var o = new PersonalBeaconsDecision().Decide(new PowerContext(ownerSlot: 0, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new RevealInfo(1, RevealField.ForceCorruptOnRoleRevealed, RevealVisibility.Personal, 0, true),
                new RevealInfo(2, RevealField.ForceCorruptOnRoleRevealed, RevealVisibility.Personal, 0, true),
            }, o.Effects);
        }

        // ---- LackOfAffection (LackOfAffectionDecision) — the two CROSSED branches ----------
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
    }
}
