using System;
using System.Linq;
using System.Reflection;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 5.2 — leaf-façade reconciliation, as a PERMANENT static-absence guard (NFR7).
    ///
    /// The Wave 1–3 extractions re-pointed callers DIRECTLY at their POCO cores
    /// (<c>new VictoryEvaluator().Evaluate(...)</c>, <c>new VoteTally().Resolve(...)</c>,
    /// <c>new ChainingResolver().IsNewMember(...)</c>, <c>new RoleDistributor().Distribute(...)</c>)
    /// — they never introduced a temporary <c>instance</c> strangler façade. So "remove the leaf
    /// façades" is satisfied by construction; nothing to delete.
    ///
    /// This guard freezes that property: each leaf core must stay a plain instantiable POCO with
    /// NO static singleton accessor (<c>instance</c> / <c>Instance</c>) and NO static mutable state —
    /// so a future change can't quietly re-singleton a core and re-grow the singleton graph (NFR7).
    /// Pure Domain reflection; no engine, no host.
    /// </summary>
    [Category("LeafPocoGuard")]
    public class LeafPocoNoFacadeGuardTests
    {
        private static readonly Type[] LeafCores =
        {
            typeof(VictoryEvaluator),
            typeof(VoteTally),
            typeof(ChainingResolver),
            typeof(RoleDistributor),
        };

        [Test]
        public void LeafCores_HaveNoSingletonAccessor()
        {
            foreach (var core in LeafCores)
            {
                foreach (var name in new[] { "instance", "Instance" })
                {
                    Assert.IsNull(core.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
                        $"{core.Name} grew a static '{name}' field — a singleton façade (NFR7). Leaf cores must stay plain POCOs.");
                    Assert.IsNull(core.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
                        $"{core.Name} grew a static '{name}' property — a singleton façade (NFR7). Leaf cores must stay plain POCOs.");
                }
            }
        }

        [Test]
        public void LeafCores_AreInstantiablePocos()
        {
            foreach (var core in LeafCores)
            {
                Assert.IsFalse(core.IsAbstract, $"{core.Name} must be concrete.");
                Assert.IsNotNull(core.GetConstructor(Type.EmptyTypes),
                    $"{core.Name} must keep a public parameterless constructor (consumed as new {core.Name}()).");
            }
        }

        [Test]
        public void LeafCores_CarryNoStaticMutableState()
        {
            foreach (var core in LeafCores)
            {
                var mutableStatics = core
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .Where(f => !f.IsLiteral && !f.IsInitOnly) // allow const + static readonly
                    .Select(f => f.Name)
                    .ToArray();

                CollectionAssert.IsEmpty(mutableStatics,
                    $"{core.Name} carries mutable static state ({string.Join(", ", mutableStatics)}) — a POCO core must be " +
                    "stateless/instance-scoped so it is deterministically unit-testable (NFR7).");
            }
        }
    }
}
