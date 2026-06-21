using System;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 11.1 — EditMode characterization of <see cref="PowerUsability.CanUse"/>, the pure
    /// extraction of the base <c>Power.CanUse</c> eligibility rule chain. Pins every disqualifier, the
    /// ignore-currently-used override, the awakening gate, the use-count boundary, and — crucially — the
    /// ordered short-circuit: the lazy target-availability delegate is invoked ONLY when a target-selecting
    /// power has passed every earlier rule, and never otherwise.
    /// </summary>
    [Category("PowerUsability")]
    public class PowerUsabilityTests
    {
        private readonly PowerUsability _usability = new();

        private static readonly Func<bool> HasTargets = () => true;
        private static readonly Func<bool> NoTargets = () => false;
        private static readonly Func<bool> TargetCheckMustNotRun =
            () => throw new InvalidOperationException("the target-availability check must not be evaluated here");

        /// <summary>A context where every rule passes; tests flip one field to assert each disqualifier.</summary>
        private static PowerUsabilityContext Passing(
            bool allComponentsAllowUse = true,
            bool isPassive = false,
            bool isCurrentlyUsed = false,
            bool ignoreCurrentlyUsed = false,
            bool isChained = false,
            bool isEliminated = false,
            bool hasToBeAwakened = false,
            bool isAwakened = false,
            bool needsTargetSelection = false,
            int powerUsesLeft = 1)
            => new PowerUsabilityContext(
                allComponentsAllowUse, isPassive, isCurrentlyUsed, ignoreCurrentlyUsed,
                isChained, isEliminated, hasToBeAwakened, isAwakened, needsTargetSelection, powerUsesLeft);

        [Test]
        public void AllRulesPass_ReturnsTrue()
        {
            Assert.IsTrue(_usability.CanUse(Passing(), HasTargets));
        }

        [Test]
        public void AComponentBlocks_ReturnsFalse()
        {
            Assert.IsFalse(_usability.CanUse(Passing(allComponentsAllowUse: false), HasTargets));
        }

        [Test]
        public void Passive_ReturnsFalse()
        {
            Assert.IsFalse(_usability.CanUse(Passing(isPassive: true), HasTargets));
        }

        [Test]
        public void CurrentlyUsed_ReturnsFalse()
        {
            Assert.IsFalse(_usability.CanUse(Passing(isCurrentlyUsed: true), HasTargets));
        }

        [Test]
        public void CurrentlyUsed_ButIgnored_ReturnsTrue()
        {
            Assert.IsTrue(_usability.CanUse(Passing(isCurrentlyUsed: true, ignoreCurrentlyUsed: true), HasTargets));
        }

        [Test]
        public void Chained_ReturnsFalse()
        {
            Assert.IsFalse(_usability.CanUse(Passing(isChained: true), HasTargets));
        }

        [Test]
        public void Eliminated_ReturnsFalse()
        {
            Assert.IsFalse(_usability.CanUse(Passing(isEliminated: true), HasTargets));
        }

        [Test]
        public void NotAwakened_WhenAwakeningRequired_ReturnsFalse()
        {
            Assert.IsFalse(_usability.CanUse(Passing(hasToBeAwakened: true, isAwakened: false), HasTargets));
        }

        [Test]
        public void Awakened_WhenAwakeningRequired_ReturnsTrue()
        {
            Assert.IsTrue(_usability.CanUse(Passing(hasToBeAwakened: true, isAwakened: true), HasTargets));
        }

        [Test]
        public void NotAwakened_WhenAwakeningNotRequired_ReturnsTrue()
        {
            // awakening state is irrelevant when the power does not require it
            Assert.IsTrue(_usability.CanUse(Passing(hasToBeAwakened: false, isAwakened: false), HasTargets));
        }

        [Test]
        public void NeedsTarget_WithNoValidTarget_ReturnsFalse()
        {
            Assert.IsFalse(_usability.CanUse(Passing(needsTargetSelection: true), NoTargets));
        }

        [Test]
        public void NeedsTarget_WithAValidTarget_ReturnsTrue()
        {
            Assert.IsTrue(_usability.CanUse(Passing(needsTargetSelection: true), HasTargets));
        }

        [Test]
        public void ZeroUsesLeft_ReturnsFalse()
        {
            Assert.IsFalse(_usability.CanUse(Passing(powerUsesLeft: 0), HasTargets));
        }

        [Test]
        public void NegativeUsesLeft_ReturnsFalse()
        {
            Assert.IsFalse(_usability.CanUse(Passing(powerUsesLeft: -1), HasTargets));
        }

        [Test]
        public void OneUseLeft_ReturnsTrue()
        {
            Assert.IsTrue(_usability.CanUse(Passing(powerUsesLeft: 1), HasTargets));
        }

        [Test]
        public void TargetCheck_NotInvoked_WhenPowerDoesNotSelectTargets()
        {
            // needsTargetSelection == false ⇒ the lazy delegate must never run (a throwing one proves it).
            Assert.IsTrue(_usability.CanUse(Passing(needsTargetSelection: false), TargetCheckMustNotRun));
        }

        [Test]
        public void TargetCheck_NotInvoked_WhenAnEarlierRuleAlreadyDisqualifies()
        {
            // Passive disqualifies before the target rule, so even a target-selecting power must NOT
            // evaluate the (expensive, engine-coupled) target check — preserving the original short-circuit.
            Assert.IsFalse(_usability.CanUse(
                Passing(isPassive: true, needsTargetSelection: true), TargetCheckMustNotRun));
        }

        [Test]
        public void TargetCheck_RunsBeforeUseCountRule_EvenWhenUsesAreExhausted()
        {
            // Ordering proof: the target rule (CanUse line 32) is evaluated BEFORE the use-count rule (line 33).
            // With uses = 0, if the order were reversed CanUse would return false first and never touch the
            // delegate. A throwing delegate proves the target check runs ahead of the later use-count rule.
            Assert.Throws<InvalidOperationException>(() =>
                _usability.CanUse(Passing(needsTargetSelection: true, powerUsesLeft: 0), TargetCheckMustNotRun));
        }
    }
}
