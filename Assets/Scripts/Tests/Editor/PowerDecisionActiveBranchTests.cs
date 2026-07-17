using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode-pure branch coverage (catalog A2 "à implémenter") for the active decisions whose branches read
    /// only the roster — Omniscience (hack gate) and HighPriorityBounty (robot vs not). Assert on the returned
    /// EffectDescriptor contract by value.
    /// </summary>
    [Category("PowerDecision")]
    public class PowerDecisionActiveBranchTests
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

        // ---- Omniscience (OmniscienceDecision) — hack only on a chosen target -------------
        [Test]
        public void Omniscience_MarginalTarget_RevealsRole_NoHack()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.marginal;
            var o = new OmniscienceDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                RequestCharacterRefresh.Instance,
            }, o.Effects);
        }

        [Test]
        public void Omniscience_NullRoster_TreatedAsNonChosen_RevealsRole_NoHack()
        {
            var o = new OmniscienceDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                RequestCharacterRefresh.Instance,
            }, o.Effects);
        }

        [Test]
        public void Omniscience_ChosenTarget_StoresHack_RevealsRoleAndHacked()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.chosen;
            var o = new OmniscienceDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 1),
                new StoreHackTarget(1),
                new RevealInfo(1, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true),
                new RevealInfo(1, RevealField.Hacked, RevealVisibility.Personal, 0, true),
                RequestCharacterRefresh.Instance,
            }, o.Effects);
        }

        // ---- HighPriorityBounty (HighPriorityBountyDecision) — robot vs not ----------------
        [Test]
        public void HighPriorityBounty_NonRobotTarget_ChainsOwner_WarnsPrivately()
        {
            var r = new FakeRoster(); // target 1 is not a robot
            var o = new HighPriorityBountyDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 0),
                new AddToChain(0),
                new ChatBroadcast("Votre cible n'était pas le robot. Vous serez enchaîné à la fin de l'éveil.",
                    ChatWindows.Server, PowerEffectAudience.Specific(0)),
                RequestCharacterRefresh.Instance,
            }, o.Effects);
        }

        [Test]
        public void HighPriorityBounty_RobotTarget_Eliminates_Broadcasts_RevealsPublic()
        {
            var r = new FakeRoster();
            r.Robots.Add(1); r.Pseudos[1] = "Bot"; r.RoleNames[0] = "Hunter";
            var o = new HighPriorityBountyDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r));
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new NewTargeting(0, 0),
                new SetEliminated(1),
                new ChatBroadcast("Bot était le robot et a été éliminé par Hunter.",
                    ChatWindows.Server, PowerEffectAudience.All),
                new RevealPublic(1, RevealField.RoleRevealed),
                RequestCharacterRefresh.Instance,
            }, o.Effects);
        }
    }
}
