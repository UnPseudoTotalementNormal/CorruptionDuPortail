using System;
using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode-pure coverage of PowerOutcome.Verdict — the caster-facing "did my power work?" grade the
    /// playtest asked for. Every verdict-bearing decision is pinned on BOTH branches, and the whole
    /// no-verdict family is pinned to None so an unconditional-effect power can never start lying to the
    /// caster. Pure domain: no NetworkManager, no MonoBehaviour.
    /// </summary>
    [Category("PowerDecision")]
    public class PowerVerdictTests
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
            public readonly HashSet<int> Chained = new();
            public readonly HashSet<int> Eliminated = new();
            public readonly Dictionary<int, string> RoleNames = new();
            public Characters.FactionType FactionOf(int slot) => Factions.TryGetValue(slot, out var f) ? f : default;
            public string PseudoOf(int slot) => Pseudos.TryGetValue(slot, out var p) ? p : "";
            public bool SameRole(int a, int b) => Roles.TryGetValue(a, out var ra) && Roles.TryGetValue(b, out var rb) && ra == rb;
            public bool IsRobot(int slot) => Robots.Contains(slot);
            public bool IsHealed(int slot) => Healed.Contains(slot);
            public bool IsCorrupted(int slot) => Corrupted.Contains(slot);
            public bool IsChained(int slot) => Chained.Contains(slot);
            public bool IsEliminated(int slot) => Eliminated.Contains(slot);
            public string RoleNameOf(int slot) => RoleNames.TryGetValue(slot, out var n) ? n : "";
        }

        private sealed class FakeState : IPowerStateResolver
        {
            private readonly Dictionary<Type, object> _map = new();
            public FakeState With<T>(T impl) where T : class { _map[typeof(T)] = impl; return this; }
            public TPort Resolve<TPort>() where TPort : class => _map.TryGetValue(typeof(TPort), out var v) ? (TPort)v : null;
        }

        private sealed class FakeCards : ICardsShufflingGuess
        {
            public bool IsCorrect { get; set; }
            public string ClickedPseudo { get; set; } = "p";
            public string GuessRoleName { get; set; } = "r";
            public IReadOnlyList<string> TargetedRoleNames { get; set; } = new string[0];
        }

        private sealed class FakeVision : IVisionGuesses
        {
            public IReadOnlyList<VisionGuess> Guesses { get; set; } = new VisionGuess[0];
        }

        /// <summary>Roster where slots 1 and 2 share a role — the "guessed right" fixture for char+role powers.</summary>
        private static FakeRoster MatchingRoles()
        {
            var r = new FakeRoster();
            r.Roles[1] = 7; r.Roles[2] = 7;
            return r;
        }

        /// <summary>Roster where slots 1 and 2 hold different roles — the "guessed wrong" fixture.</summary>
        private static FakeRoster MismatchedRoles()
        {
            var r = new FakeRoster();
            r.Roles[1] = 7; r.Roles[2] = 9;
            return r;
        }

        private static PowerContext CharAndRole(IRosterView roster) =>
            new(ownerSlot: 0, targetSlot: 1, secondaryTargetSlot: 2, roster: roster);

        // ---- Role-guess powers ------------------------------------------------------------------------

        [Test]
        public void Blessing_RoleMatch_IsCorrect() =>
            Assert.AreEqual(PowerVerdict.Correct, new BlessingDecision().Decide(CharAndRole(MatchingRoles())).Verdict);

        [Test]
        public void Blessing_RoleMismatch_IsIncorrect() =>
            Assert.AreEqual(PowerVerdict.Incorrect, new BlessingDecision().Decide(CharAndRole(MismatchedRoles())).Verdict);

        [Test]
        public void ChainedByShadows_RoleMatch_IsCorrect() =>
            Assert.AreEqual(PowerVerdict.Correct, new ChainedByShadowsDecision().Decide(CharAndRole(MatchingRoles())).Verdict);

        [Test]
        public void ChainedByShadows_RoleMismatch_IsIncorrect() =>
            Assert.AreEqual(PowerVerdict.Incorrect, new ChainedByShadowsDecision().Decide(CharAndRole(MismatchedRoles())).Verdict);

        [Test]
        public void EmbraceOfShadows_RoleMatch_IsCorrect() =>
            Assert.AreEqual(PowerVerdict.Correct, new EmbraceOfShadowsDecision().Decide(CharAndRole(MatchingRoles())).Verdict);

        [Test]
        public void EmbraceOfShadows_RoleMismatch_IsIncorrect() =>
            Assert.AreEqual(PowerVerdict.Incorrect, new EmbraceOfShadowsDecision().Decide(CharAndRole(MismatchedRoles())).Verdict);

        // ---- DroolyHealing (Glooby): graded on the ROLE GUESS ALONE ------------------------------------

        [Test]
        public void DroolyHealing_RoleMatch_OnCorruptedTarget_IsCorrect()
        {
            var roster = MatchingRoles();
            roster.Corrupted.Add(1);
            Assert.AreEqual(PowerVerdict.Correct, new DroolyHealingDecision().Decide(CharAndRole(roster)).Verdict);
        }

        // THE design call: a right guess on a target who happened to be healthy heals nobody, but the
        // caster's deduction was still right and the feedback must say so.
        [Test]
        public void DroolyHealing_RoleMatch_OnHealthyTarget_IsStillCorrect_AndHealsNobody()
        {
            var outcome = new DroolyHealingDecision().Decide(CharAndRole(MatchingRoles()));
            Assert.AreEqual(PowerVerdict.Correct, outcome.Verdict);
            CollectionAssert.DoesNotContain(outcome.Effects, new HealPlayer(1));
        }

        [Test]
        public void DroolyHealing_RoleMatch_OnCorruptedTarget_Heals()
        {
            var roster = MatchingRoles();
            roster.Corrupted.Add(1);
            CollectionAssert.Contains(new DroolyHealingDecision().Decide(CharAndRole(roster)).Effects, new HealPlayer(1));
        }

        [Test]
        public void DroolyHealing_RoleMismatch_IsIncorrect_AndOnlyTargets()
        {
            var outcome = new DroolyHealingDecision().Decide(CharAndRole(MismatchedRoles()));
            Assert.AreEqual(PowerVerdict.Incorrect, outcome.Verdict);
            CollectionAssert.AreEqual(new EffectDescriptor[] { new NewTargeting(0, 1) }, outcome.Effects);
        }

        // ---- Faction-bet powers -----------------------------------------------------------------------

        [Test]
        public void TruthChains_AnomalyTarget_IsCorrect()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.anomaly;
            r.Pseudos[1] = "x";
            Assert.AreEqual(PowerVerdict.Correct,
                new TruthChainsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r)).Verdict);
        }

        [Test]
        public void TruthChains_NonAnomalyTarget_IsIncorrect()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.chosen;
            Assert.AreEqual(PowerVerdict.Incorrect,
                new TruthChainsDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r)).Verdict);
        }

        [Test]
        public void Omniscience_ChosenTarget_IsCorrect()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.chosen;
            Assert.AreEqual(PowerVerdict.Correct,
                new OmniscienceDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r)).Verdict);
        }

        [Test]
        public void Omniscience_NonChosenTarget_IsIncorrect()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.anomaly;
            Assert.AreEqual(PowerVerdict.Incorrect,
                new OmniscienceDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r)).Verdict);
        }

        [Test]
        public void HighPriorityBounty_HitsRobot_IsCorrect()
        {
            var r = new FakeRoster();
            r.Robots.Add(1); r.Pseudos[1] = "x"; r.RoleNames[0] = "y";
            Assert.AreEqual(PowerVerdict.Correct,
                new HighPriorityBountyDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r)).Verdict);
        }

        [Test]
        public void HighPriorityBounty_MissesRobot_IsIncorrect() =>
            Assert.AreEqual(PowerVerdict.Incorrect,
                new HighPriorityBountyDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, roster: new FakeRoster())).Verdict);

        // ---- State-port guess powers -------------------------------------------------------------------

        [Test]
        public void CardsShuffling_CorrectGuess_IsCorrect()
        {
            var state = new FakeState().With<ICardsShufflingGuess>(new FakeCards { IsCorrect = true });
            Assert.AreEqual(PowerVerdict.Correct,
                new CardsShufflingDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, state: state)).Verdict);
        }

        [Test]
        public void CardsShuffling_WrongGuess_IsIncorrect()
        {
            var state = new FakeState().With<ICardsShufflingGuess>(new FakeCards { IsCorrect = false });
            Assert.AreEqual(PowerVerdict.Incorrect,
                new CardsShufflingDecision().Decide(new PowerContext(ownerSlot: 0, targetSlot: 1, state: state)).Verdict);
        }

        [Test]
        public void VisionOfTheImpossible_AnyGuessMatches_IsCorrect()
        {
            var state = new FakeState().With<IVisionGuesses>(new FakeVision
            {
                Guesses = new[] { new VisionGuess(1, false, "a"), new VisionGuess(2, true, "b") }
            });
            Assert.AreEqual(PowerVerdict.Correct,
                new VisionOfTheImpossibleDecision().Decide(new PowerContext(ownerSlot: 0, state: state)).Verdict);
        }

        [Test]
        public void VisionOfTheImpossible_NoGuessMatches_IsIncorrect()
        {
            var state = new FakeState().With<IVisionGuesses>(new FakeVision
            {
                Guesses = new[] { new VisionGuess(1, false, "a"), new VisionGuess(2, false, "b") }
            });
            Assert.AreEqual(PowerVerdict.Incorrect,
                new VisionOfTheImpossibleDecision().Decide(new PowerContext(ownerSlot: 0, state: state)).Verdict);
        }

        // ---- No-verdict family: must stay silent -------------------------------------------------------
        // These powers make no guess the caster can get wrong (unconditional effects, or information
        // queries whose "negative" answer is just as useful as the positive one). Grading them would fire
        // a red 🚫 at a player who did nothing wrong.

        [Test]
        public void UnconditionalPowers_HaveNoVerdict()
        {
            var roster = new FakeRoster { Slots = new[] { 0, 1 } };
            var ctx = new PowerContext(ownerSlot: 0, targetSlot: 1, roster: roster);

            Assert.AreEqual(PowerVerdict.None, new AutoCorruptionDecision().Decide(ctx).Verdict, "AutoCorruption");
            Assert.AreEqual(PowerVerdict.None, new CorruptionParanoiaDecision().Decide(ctx).Verdict, "CorruptionParanoia");
            Assert.AreEqual(PowerVerdict.None, new CorruptionInsightDecision().Decide(ctx).Verdict, "CorruptionInsight");
            Assert.AreEqual(PowerVerdict.None, new CorruptingMarkDecision().Decide(ctx).Verdict, "CorruptingMark");
        }

        // CursedVision and LackOfAffection deliberately stay None: neither asks the caster to guess.
        // CursedVision is an information query ("est / n'est pas un élu" — both answers are a result), and
        // LackOfAffection's outcome depends on WHO was contacted, not on a prediction the caster made.
        [Test]
        public void InformationPowers_HaveNoVerdict()
        {
            var r = new FakeRoster();
            r.Factions[1] = Characters.FactionType.anomaly;
            r.Pseudos[1] = "x"; r.RoleNames[0] = "y";
            var ctx = new PowerContext(ownerSlot: 0, targetSlot: 1, roster: r);

            Assert.AreEqual(PowerVerdict.None, new CursedVisionDecision().Decide(ctx).Verdict, "CursedVision");
            Assert.AreEqual(PowerVerdict.None, new LackOfAffectionDecision().Decide(ctx).Verdict, "LackOfAffection");
        }

        [Test]
        public void PowerOutcome_DefaultsToNoVerdict()
        {
            Assert.AreEqual(PowerVerdict.None, PowerOutcome.Accept(new NewTargeting(0, 1)).Verdict);
            Assert.AreEqual(PowerVerdict.None, PowerOutcome.AcceptEmpty().Verdict);
            Assert.AreEqual(PowerVerdict.None, PowerOutcome.Reject("nope").Verdict);
        }
    }
}
