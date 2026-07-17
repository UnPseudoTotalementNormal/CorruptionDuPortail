using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Tests.PlayMode.Infra
{
    /// <summary>
    /// THE GATE (R8): proves <see cref="TestStaticReset.ResetAll"/> actually wipes the production static
    /// inventory. If this fails, no PlayMode batch may be written on top of it — the teardown barrier is not
    /// trustworthy, and domain-reload-OFF means the leak becomes an order-dependent, non-reproducible flake.
    /// </summary>
    public class FixtureResetTests
    {
        private GameObject _dirtyGo;

        [TearDown]
        public void Cleanup()
        {
            // Wipe first (so any component we dirtied is unhooked), then destroy the never-activated carrier.
            TestStaticReset.ResetAll();
            if (_dirtyGo != null)
            {
                UnityEngine.Object.DestroyImmediate(_dirtyGo);
                _dirtyGo = null;
            }
        }

        [Test]
        public void ResetAll_ClearsLeakyStaticCollections()
        {
            object bound = TestStaticReset.GetStaticCollection("PBoundByInk", "usedBoundByInkIds");
            object existing = TestStaticReset.GetStaticCollection("DontDestroyOnLoadComponent", "existingIds");
            Assert.IsNotNull(bound, "PBoundByInk.usedBoundByInkIds not found — the inventory drifted from the code.");
            Assert.IsNotNull(existing, "DontDestroyOnLoadComponent.existingIds not found — the inventory drifted.");

            Add(bound, 424242);
            Add(existing, 424242);
            Assert.Greater(Count(bound), 0, "Failed to dirty usedBoundByInkIds (reflection Add).");
            Assert.Greater(Count(existing), 0, "Failed to dirty existingIds (reflection Add).");

            TestStaticReset.ResetAll();

            Assert.AreEqual(0, Count(bound), "ResetAll must clear usedBoundByInkIds.");
            Assert.AreEqual(0, Count(existing), "ResetAll must clear existingIds.");
        }

        [Test]
        public void ResetAll_NullsEveryResolvableSingleton()
        {
            // Inactive carrier: AddComponent does NOT run Awake, so no singleton-claim side effects and no NRE
            // from managers that assert scene wiring in Awake/Start.
            _dirtyGo = new GameObject("FixtureResetDirty");
            _dirtyGo.SetActive(false);

            var dirtied = new List<Type>();
            var unresolved = new List<string>();

            foreach (string name in TestStaticReset.SingletonTypeNames)
            {
                Type t = TestStaticReset.ResolveType(name);
                if (t == null)
                {
                    unresolved.Add(name);
                    continue;
                }
                if (!typeof(Component).IsAssignableFrom(t))
                {
                    // Non-Component singleton: ResetAll still nulls it, but we can't cheaply instantiate one to
                    // dirty it here. Not gate-proven, but not a leak risk either.
                    continue;
                }

                Component c;
                try { c = _dirtyGo.AddComponent(t); }
                catch { continue; } // uninstantiable in isolation (RequireComponent chain, abstract, …) — skip.
                if (c == null) continue;

                TestStaticReset.SetStaticInstance(t, c);
                if (ReferenceEquals(TestStaticReset.GetStaticInstance(t), c))
                {
                    dirtied.Add(t);
                }
            }

            Assert.IsEmpty(unresolved,
                "Inventory names that no longer resolve to a type (rename/move?): " + string.Join(", ", unresolved));
            Assert.Greater(dirtied.Count, 0, "Could not dirty ANY singleton — the gate would prove nothing.");

            TestStaticReset.ResetAll();

            // At assert time the components are still alive (destroyed only in teardown), so Unity's fake-null
            // cannot mask a leak: a missed singleton reads as a live, non-null component.
            List<string> leaked = dirtied
                .Where(t => TestStaticReset.GetStaticInstance(t) != null)
                .Select(t => t.Name)
                .ToList();
            Assert.IsEmpty(leaked, "ResetAll left these singletons non-null: " + string.Join(", ", leaked));
        }

        private static void Add(object collection, int value) =>
            collection.GetType().GetMethod("Add", new[] { typeof(int) })?.Invoke(collection, new object[] { value });

        private static int Count(object collection) =>
            (int)collection.GetType().GetProperty("Count").GetValue(collection);
    }
}
