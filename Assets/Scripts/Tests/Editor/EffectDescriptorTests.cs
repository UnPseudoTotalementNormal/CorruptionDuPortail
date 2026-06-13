using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 4.0 structural-invariant tests for the <see cref="EffectDescriptor"/> closed union:
    /// value equality (same type + same payload), payload-driven inequality, type-driven
    /// inequality (two variants with identical payload are NOT equal), hash/equality coherence,
    /// audience value semantics, and the singleton variants. Pure Domain — no engine, no host.
    /// </summary>
    [Category("PowerEffect")]
    public class EffectDescriptorTests
    {
        [Test]
        public void SamePayload_AreValueEqual()
        {
            Assert.AreEqual(new NewTargeting(0, 3), new NewTargeting(0, 3));
            Assert.IsTrue(new NewTargeting(0, 3) == new NewTargeting(0, 3));
            Assert.AreEqual(new NewTargeting(0, 3).GetHashCode(), new NewTargeting(0, 3).GetHashCode());
        }

        [Test]
        public void DifferentPayload_AreNotEqual()
        {
            Assert.AreNotEqual(new NewTargeting(0, 3), new NewTargeting(0, 4));
            Assert.IsTrue(new NewTargeting(0, 3) != new NewTargeting(1, 3));
            Assert.AreNotEqual(new CorruptPlayer(1), new CorruptPlayer(2));
        }

        [Test]
        public void DifferentVariant_SamePayload_AreNotEqual()
        {
            // The discriminator is the TYPE: CorruptionSucceeded(1) and CorruptionFailed(1)
            // share an int payload but must never compare equal (else a golden trace that
            // swaps success<->fail would pass silently).
            EffectDescriptor succeeded = new CorruptionSucceeded(1);
            EffectDescriptor failed = new CorruptionFailed(1);
            Assert.AreNotEqual(succeeded, failed);
            Assert.IsTrue(succeeded != failed);
        }

        [Test]
        public void RevealInfo_AllFiveComponents_ParticipateInEquality()
        {
            var baseline = new RevealInfo(2, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true);
            Assert.AreEqual(baseline, new RevealInfo(2, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true));
            Assert.AreNotEqual(baseline, new RevealInfo(3, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true));
            Assert.AreNotEqual(baseline, new RevealInfo(2, RevealField.CorruptRevealed, RevealVisibility.Personal, 0, true));
            Assert.AreNotEqual(baseline, new RevealInfo(2, RevealField.RoleRevealed, RevealVisibility.Public, 0, true));
            Assert.AreNotEqual(baseline, new RevealInfo(2, RevealField.RoleRevealed, RevealVisibility.Personal, 1, true));
            Assert.AreNotEqual(baseline, new RevealInfo(2, RevealField.RoleRevealed, RevealVisibility.Personal, 0, false));
        }

        [Test]
        public void SingletonVariants_AreEqualAcrossInstances()
        {
            Assert.AreEqual(UnfocusAll.Instance, new UnfocusAll());
            Assert.AreEqual(DecrementUses.Instance, new DecrementUses());
            Assert.AreEqual(RequestCharacterRefresh.Instance, new RequestCharacterRefresh());
            Assert.AreEqual(DestroyAllArrows.Instance, new DestroyAllArrows());
            // Distinct singleton variants never collide.
            Assert.AreNotEqual((EffectDescriptor)UnfocusAll.Instance, (EffectDescriptor)DecrementUses.Instance);
        }

        [Test]
        public void Audience_ValueSemantics()
        {
            Assert.AreEqual(PowerEffectAudience.All, PowerEffectAudience.All);
            Assert.AreEqual(PowerEffectAudience.Specific(5), PowerEffectAudience.Specific(5));
            Assert.AreNotEqual(PowerEffectAudience.Specific(5), PowerEffectAudience.Specific(6));
            Assert.AreNotEqual(PowerEffectAudience.All, PowerEffectAudience.Owner);
            Assert.AreNotEqual(PowerEffectAudience.Owner, PowerEffectAudience.Specific(0));
        }

        [Test]
        public void Audience_ParticipatesInDescriptorEquality()
        {
            Assert.AreEqual(
                new DiscoverChat(7, "Lié par l'encre", PowerEffectAudience.Specific(2)),
                new DiscoverChat(7, "Lié par l'encre", PowerEffectAudience.Specific(2)));
            Assert.AreNotEqual(
                new DiscoverChat(7, "Lié par l'encre", PowerEffectAudience.Specific(2)),
                new DiscoverChat(7, "Lié par l'encre", PowerEffectAudience.Owner));
        }

        [Test]
        public void Equals_Null_And_WrongType_AreFalse()
        {
            EffectDescriptor d = new CorruptPlayer(1);
            Assert.IsFalse(d.Equals(null));
            Assert.IsFalse(d.Equals("not a descriptor"));
            Assert.IsFalse(d == null);
            Assert.IsTrue(d != null);
        }

        [Test]
        public void ToString_IsStableAndReadable_ForGoldenTraces()
        {
            Assert.AreEqual("UnfocusAll", UnfocusAll.Instance.ToString());
            Assert.AreEqual("NewTargeting(0, 3)", new NewTargeting(0, 3).ToString());
            Assert.AreEqual("DiscoverChat(7, ink, Specific(2))", new DiscoverChat(7, "ink", PowerEffectAudience.Specific(2)).ToString());
            Assert.AreEqual("RevealInfo(2, RoleRevealed, Personal, 0, True)",
                new RevealInfo(2, RevealField.RoleRevealed, RevealVisibility.Personal, 0, true).ToString());
        }

        [Test]
        public void DescriptorList_OrderIsObservable_ViaSequenceEqual()
        {
            // The list IS the contract (NFR4): order matters and is value-comparable.
            var a = new List<EffectDescriptor> { new NewTargeting(0, 1), new CorruptPlayer(1), UnfocusAll.Instance };
            var b = new List<EffectDescriptor> { new NewTargeting(0, 1), new CorruptPlayer(1), UnfocusAll.Instance };
            var reordered = new List<EffectDescriptor> { new CorruptPlayer(1), new NewTargeting(0, 1), UnfocusAll.Instance };
            CollectionAssert.AreEqual(a, b);
            CollectionAssert.AreNotEqual(a, reordered);
        }
    }
}
