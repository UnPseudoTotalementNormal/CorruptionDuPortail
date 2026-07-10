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
    }
}
