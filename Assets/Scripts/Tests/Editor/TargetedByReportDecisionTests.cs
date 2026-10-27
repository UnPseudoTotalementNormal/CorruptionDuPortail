using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.PlayerIcons;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Orpheline passive (Lot C.2) — EditMode characterization of TargetedByReportDecision. The report port
    /// is a hand-built fake (no host, no NGO); assert the ordered AddPlayerIcon list by value: one per
    /// élu targeter, each marking the TARGETER's slot with the owner as the sole viewer.
    /// </summary>
    [Category("PowerDecision")]
    public class TargetedByReportDecisionTests
    {
        private sealed class FakeState : IPowerStateResolver
        {
            private readonly Dictionary<System.Type, object> _map = new();
            public FakeState With<T>(T impl) where T : class { _map[typeof(T)] = impl; return this; }
            public TPort Resolve<TPort>() where TPort : class => _map.TryGetValue(typeof(TPort), out var v) ? (TPort)v : null;
        }

        private sealed class FakeTargetedBy : ITargetedByReport
        {
            public IReadOnlyList<int> TargeterSlots { get; set; } = new int[0];
        }

        // Only FactionOf matters here: the decision keeps the élu (chosen) targeters. Unlisted slots are élus.
        private sealed class FakeRoster : IRosterView
        {
            public readonly Dictionary<int, Characters.FactionType> Factions = new();
            public IReadOnlyList<int> Slots => new int[0];
            public Characters.FactionType FactionOf(int slot) =>
                Factions.TryGetValue(slot, out var f) ? f : Characters.FactionType.chosen;
            public string PseudoOf(int slot) => "";
            public bool SameRole(int a, int b) => false;
            public bool IsRobot(int slot) => false;
            public bool IsHealed(int slot) => false;
            public bool IsCorrupted(int slot) => false;
            public bool IsChained(int slot) => false;
            public bool IsEliminated(int slot) => false;
            public bool IsFake(int slot) => false;
            public string RoleNameOf(int slot) => "";
        }

        [Test]
        public void Passive_IdAndFlag()
        {
            var decision = new TargetedByReportDecision();
            Assert.IsTrue(decision.IsPassive);
            Assert.AreEqual(PowerId.TargetedByReport, decision.Id);
        }

        [Test]
        public void NoTargeters_AcceptsEmpty()
        {
            var state = new FakeState().With<ITargetedByReport>(new FakeTargetedBy { TargeterSlots = new int[0] });
            var outcome = new TargetedByReportDecision { IconId = 77 }
                .Decide(new PowerContext(ownerSlot: 3, roster: new FakeRoster(), state: state));

            Assert.IsTrue(outcome.Accepted);
            CollectionAssert.IsEmpty(outcome.Effects);
        }

        [Test]
        public void ThreeTargeters_OneIconEach_MarksTargeterViewerIsOwner()
        {
            const ulong iconId = 4242;
            const int owner = 2;
            var state = new FakeState().With<ITargetedByReport>(
                new FakeTargetedBy { TargeterSlots = new[] { 5, 8, 1 } });

            var outcome = new TargetedByReportDecision { IconId = iconId }
                .Decide(new PowerContext(ownerSlot: owner, roster: new FakeRoster(), state: state));

            Assert.IsTrue(outcome.Accepted);
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new AddPlayerIcon(iconId, 5, owner, PlayerIconLifetime.ClearAtAwakeningStart),
                new AddPlayerIcon(iconId, 8, owner, PlayerIconLifetime.ClearAtAwakeningStart),
                new AddPlayerIcon(iconId, 1, owner, PlayerIconLifetime.ClearAtAwakeningStart),
            }, outcome.Effects);
        }

        [Test]
        public void MarkedSlotIsTargeter_NotOwner()
        {
            const int owner = 2;
            const int targeter = 9;
            var state = new FakeState().With<ITargetedByReport>(
                new FakeTargetedBy { TargeterSlots = new[] { targeter } });

            var outcome = new TargetedByReportDecision { IconId = 1 }
                .Decide(new PowerContext(ownerSlot: owner, roster: new FakeRoster(), state: state));

            var icon = (AddPlayerIcon)outcome.Effects[0];
            Assert.AreEqual(targeter, icon.MarkedSlot, "the icon lands on the TARGETER's thumbnail");
            Assert.AreEqual(owner, icon.ViewerSlot, "only the Orpheline (owner) sees it");
        }

        [Test]
        public void NonEluTargeters_AreHidden_OnlyElusMarked()
        {
            const int owner = 2;
            var roster = new FakeRoster();
            roster.Factions[5] = Characters.FactionType.anomaly;
            roster.Factions[8] = Characters.FactionType.chosen;
            roster.Factions[1] = Characters.FactionType.marginal;
            var state = new FakeState().With<ITargetedByReport>(
                new FakeTargetedBy { TargeterSlots = new[] { 5, 8, 1 } });

            var outcome = new TargetedByReportDecision { IconId = 7 }
                .Decide(new PowerContext(ownerSlot: owner, roster: roster, state: state));

            Assert.IsTrue(outcome.Accepted);
            CollectionAssert.AreEqual(new EffectDescriptor[]
            {
                new AddPlayerIcon(7, 8, owner, PlayerIconLifetime.ClearAtAwakeningStart),
            }, outcome.Effects, "only the élu targeter is shown, anomaly and marginal stay hidden");
        }

        [Test]
        public void OnlyNonEluTargeters_AcceptsEmpty()
        {
            var roster = new FakeRoster();
            roster.Factions[4] = Characters.FactionType.anomaly;
            var state = new FakeState().With<ITargetedByReport>(new FakeTargetedBy { TargeterSlots = new[] { 4 } });

            var outcome = new TargetedByReportDecision { IconId = 7 }
                .Decide(new PowerContext(ownerSlot: 2, roster: roster, state: state));

            Assert.IsTrue(outcome.Accepted);
            CollectionAssert.IsEmpty(outcome.Effects);
        }
    }
}
