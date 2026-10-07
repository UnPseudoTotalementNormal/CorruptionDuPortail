using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>NET-00 — the public-state digest: canonical, per-component, order-aware only where order is state.</summary>
    [Category("Desync")]
    public class DesyncDigestTests
    {
        private static PublicStateProjection Projection(string[] roster, string[] flags)
        {
            var _p = new PublicStateProjection();
            _p.SetComponent("Roster", roster, ordered: true);
            _p.SetComponent("CharacterFlags", flags, ordered: false);
            return _p;
        }

        [Test]
        public void SameState_SameHashes()
        {
            var _a = DesyncDigest.ComputeComponentHashes(Projection(new[] { "0|A", "1|B" }, new[] { "0|c", "1|d" }));
            var _b = DesyncDigest.ComputeComponentHashes(Projection(new[] { "0|A", "1|B" }, new[] { "0|c", "1|d" }));
            Assert.IsEmpty(DesyncDigest.Mismatches(_a, _b));
        }

        [Test]
        public void UnorderedComponent_InputOrderDoesNotMatter()
        {
            var _a = DesyncDigest.ComputeComponentHashes(Projection(new[] { "0|A" }, new[] { "0|c", "1|d" }));
            var _b = DesyncDigest.ComputeComponentHashes(Projection(new[] { "0|A" }, new[] { "1|d", "0|c" }));
            Assert.IsEmpty(DesyncDigest.Mismatches(_a, _b));
        }

        [Test]
        public void OrderedComponent_OrderIsState()
        {
            var _a = DesyncDigest.ComputeComponentHashes(Projection(new[] { "0|A", "1|B" }, new string[0]));
            var _b = DesyncDigest.ComputeComponentHashes(Projection(new[] { "1|B", "0|A" }, new string[0]));
            CollectionAssert.AreEqual(new[] { "Roster" }, DesyncDigest.Mismatches(_a, _b));
        }

        [Test]
        public void OneFieldDiffers_OnlyThatComponentMismatches()
        {
            var _a = DesyncDigest.ComputeComponentHashes(Projection(new[] { "0|A", "1|B" }, new[] { "0|c" }));
            var _b = DesyncDigest.ComputeComponentHashes(Projection(new[] { "0|A", "1|" }, new[] { "0|c" }));
            CollectionAssert.AreEqual(new[] { "Roster" }, DesyncDigest.Mismatches(_a, _b));
        }

        [Test]
        public void ComponentMissingOnOneSide_IsAMismatch()
        {
            var _a = DesyncDigest.ComputeComponentHashes(Projection(new[] { "0|A" }, new[] { "0|c" }));
            var _b = new Dictionary<string, ulong>(_a);
            _b.Remove("CharacterFlags");
            CollectionAssert.AreEqual(new[] { "CharacterFlags" }, DesyncDigest.Mismatches(_a, _b));
            CollectionAssert.AreEqual(new[] { "CharacterFlags" }, DesyncDigest.Mismatches(_b, _a));
        }

        [Test]
        public void EmptyComponent_IsStableAndDescribed()
        {
            var _p = new PublicStateProjection();
            _p.SetComponent("Roster", new string[0], ordered: true);
            var _h1 = DesyncDigest.ComputeComponentHashes(_p);
            var _h2 = DesyncDigest.ComputeComponentHashes(_p);
            Assert.AreEqual(_h1["Roster"], _h2["Roster"]);
            StringAssert.Contains("(empty)", DesyncDigest.Describe(_p, "Roster"));
        }

        [Test]
        public void NonAsciiNames_HashDeterministically()
        {
            var _a = DesyncDigest.ComputeComponentHashes(Projection(new[] { "0|Élodie#1", "1|ナオ" }, new string[0]));
            var _b = DesyncDigest.ComputeComponentHashes(Projection(new[] { "0|Élodie#1", "1|ナオ" }, new string[0]));
            Assert.IsEmpty(DesyncDigest.Mismatches(_a, _b));
        }
    }
}
