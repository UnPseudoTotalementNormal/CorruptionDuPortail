using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode-pure branch coverage (catalog A1/A2) for the corruption/embrace/ink/steal decisions. Assert on
    /// the returned EffectDescriptor contract by value.
    /// </summary>
    [Category("PowerDecision")]
    public class PowerDecisionCorruptionBranchTests
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

        // ---- CursedVision (CursedVisionDecision) — verdict + card branch on target faction -
        [Test]
        public void CursedVision_ChosenTarget_CardShown_EluVerdict()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.chosen; r.Pseudos[1] = "Bob";
            var o = new CursedVisionDecision { CardEffectId = 3 }.Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new CorruptPlayer(1),
                new RevealInfo(1, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, false),
                new AddCardEffect(3, 1, false),
                new ChatLocal("Bob est un élu.", ChatWindows.Server),
                new CorruptPlayer(0),
                new RevealInfo(0, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, false),
            }, o.Effects);
        }

        [Test]
        public void CursedVision_NonChosenTarget_CardHidden_NotEluVerdict()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.anomaly; r.Pseudos[1] = "Bob";
            var o = new CursedVisionDecision { CardEffectId = 3 }.Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new CorruptPlayer(1),
                new RevealInfo(1, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, false),
                new AddCardEffect(3, 1, true),
                new ChatLocal("Bob n'est pas un élu.", ChatWindows.Server),
                new CorruptPlayer(0),
                new RevealInfo(0, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, false),
            }, o.Effects);
        }

        // ---- EmbraceOfShadows (EmbraceOfShadowsDecision) — role match vs mismatch ----------
        [Test]
        public void Embrace_RoleMatch_CorruptsRevealsSucceeds()
        {
            var r = new FakeRoster();
            r.Roles[1] = 7; r.Roles[2] = 7;
            var o = new EmbraceOfShadowsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new CorruptPlayer(1),
                new CorruptionSucceeded(1),
                new RevealInfo(1, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, false),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, false),
            }, o.Effects);
        }

        [Test]
        public void Embrace_RoleMismatch_TargetsAndFails()
        {
            var r = new FakeRoster();
            r.Roles[1] = 7; r.Roles[2] = 9;
            var o = new EmbraceOfShadowsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new CorruptionFailed(1),
            }, o.Effects);
        }

        // ---- BoundByInk (BoundByInkDecision) — missing chat state falls back to -1 ---------
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

        // ---- MarqueHurluberluges (MarqueHurluberlugesDecision) — passive grant intention ---
        [Test]
        public void MarqueHurluberluges_EmitsGrantStolenPowersForOwner()
        {
            var decision = new MarqueHurluberlugesDecision();
            Assert.IsTrue(decision.IsPassive);
            Assert.AreEqual(PowerId.MarqueHurluberluges, decision.Id);
            var o = decision.Decide(new PowerContext(ownerSlot: 4));
            CollectionAssert.AreEqual(new EffectDescriptor[] { new GrantStolenPowers(4) }, o.Effects);
        }
    }
}
