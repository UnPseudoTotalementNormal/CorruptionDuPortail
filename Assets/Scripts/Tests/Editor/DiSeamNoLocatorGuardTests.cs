using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// Story 6.1 / Epic 6 (D0) — the static-absence guard that makes the despaghettification
    /// track safe. Once a consumer is rerouted off the Service Locator
    /// (<c>GameManager.instance</c> / <c>CharacterManager.instance</c>) onto an injected
    /// dependency, it is added to <see cref="MigratedConsumers"/>. The guard then FAILS if a
    /// regression ever re-introduces a locator lookup in that type — letting Epic 7 migrate the
    /// 78 <c>.characterManager</c> + 31 <c>.gameInfoRevealer</c> sites in bulk without silently
    /// back-sliding.
    ///
    /// MECHANISM CHOICE (recorded per AC 5): SOURCE scan, not IL scan. The two locators are
    /// asymmetric — <c>GameManager.instance</c> is an auto-property (compiles to a
    /// <c>call get_instance</c>) while <c>CharacterManager.instance</c> is a public static FIELD
    /// (compiles to <c>ldsfld</c>). A single IL token scan would have to special-case both the
    /// <c>call</c> opcode + a method token AND the <c>ldsfld</c> opcode + a field token, and the
    /// operand tokens are module-scoped (brittle once a migrated consumer lives in another
    /// assembly). A source substring catches BOTH uniformly in one rule. It is not hard-path
    /// coupled: the <c>.cs</c> file is located by Unity's enforced "class name == file name"
    /// convention via a recursive search under the Assets tree.
    /// </summary>
    [Category("DiSeamGuard")]
    public class DiSeamNoLocatorGuardTests
    {
        // Curated set of consumers proven to no longer use the locator. Epic 7 appends each
        // migrated type here as it is rerouted onto injection.
        private static readonly Type[] MigratedConsumers =
        {
            typeof(LightManager),
        };

        // The Service-Locator accessors a migrated consumer must never reference again.
        private static readonly string[] ForbiddenLocators =
        {
            "GameManager.instance",
            "CharacterManager.instance",
        };

        [Test]
        public void MigratedConsumers_DoNotReferenceTheLocator()
        {
            foreach (var consumer in MigratedConsumers)
            {
                string source = ReadSource(consumer);
                string[] hits = FindForbiddenLocators(source);

                CollectionAssert.IsEmpty(hits,
                    $"{consumer.Name} references the Service Locator ({string.Join(", ", hits)}) — a migrated " +
                    "consumer must receive its dependency through injection (a [SerializeField] or Initialize(...)), " +
                    "never via a static instance lookup. See the Injection Seam Convention in " +
                    "refactor-architecture-despaghetti.md.");
            }
        }

        [Test]
        public void Guard_Bites_OnSyntheticLocatorUsage()
        {
            // Proves the detection mechanism actually fires — if LightManager (or any future
            // migrated consumer) still held a locator lookup, FindForbiddenLocators would flag it.
            const string badSource = "void Start() { GameManager.instance.currentGameStateIndex.OnValueChanged += X; }";
            CollectionAssert.IsNotEmpty(FindForbiddenLocators(badSource),
                "The guard failed to detect a known locator usage — the mechanism is broken.");

            const string cleanSource = "void Start() { gameManager.currentGameStateIndex.OnValueChanged += X; }";
            CollectionAssert.IsEmpty(FindForbiddenLocators(cleanSource),
                "The guard false-positived on injected access — it must only flag the static locator.");
        }

        private static string[] FindForbiddenLocators(string source)
        {
            return ForbiddenLocators.Where(source.Contains).ToArray();
        }

        private static string ReadSource(Type type)
        {
            // Unity enforces class name == file name for MonoBehaviours, so the source file is
            // <Type.Name>.cs somewhere under Assets. Search rather than hard-code a path.
            string fileName = type.Name + ".cs";
            string[] matches = Directory.GetFiles(Application.dataPath, fileName, SearchOption.AllDirectories);

            Assert.IsNotEmpty(matches,
                $"Could not locate source file '{fileName}' for migrated consumer {type.Name} under Assets/.");
            Assert.AreEqual(1, matches.Length,
                $"Ambiguous source file for {type.Name}: found {matches.Length} '{fileName}' files.");

            return File.ReadAllText(matches[0]);
        }
    }
}
