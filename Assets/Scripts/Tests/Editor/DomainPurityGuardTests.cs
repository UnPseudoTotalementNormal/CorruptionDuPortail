using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// Permanent CI guard for Story 1.3: the <c>CorruptionDuPortail.Domain</c> assembly MUST stay pure.
    /// Reads the actual compiled asmdef manifest (not a text grep) and fails any PR that flips
    /// <c>noEngineReferences</c> to false or adds an assembly reference — Domain purity is a permanent
    /// invariant (NFR2), not an initial state.
    /// </summary>
    [Category("DomainPurity")]
    public class DomainPurityGuardTests
    {
        private const string DomainAssemblyName = "CorruptionDuPortail.Domain";

        [Serializable]
        private class AsmdefShape
        {
            public string name;
            public string[] references;
            public bool noEngineReferences;
        }

        private static AsmdefShape LoadDomainAsmdef()
        {
            var guids = AssetDatabase.FindAssets($"{DomainAssemblyName} t:AssemblyDefinitionAsset");
            Assert.That(guids, Is.Not.Empty,
                $"Could not locate the {DomainAssemblyName}.asmdef — the Domain assembly must exist.");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);
                if (asset == null)
                {
                    continue;
                }

                var shape = JsonUtility.FromJson<AsmdefShape>(asset.text);
                if (shape != null && shape.name == DomainAssemblyName)
                {
                    return shape;
                }
            }

            Assert.Fail($"Found no AssemblyDefinitionAsset named exactly '{DomainAssemblyName}'.");
            return null;
        }

        [Test]
        public void Domain_HasNoEngineReferences()
        {
            var shape = LoadDomainAsmdef();
            Assert.That(shape.noEngineReferences, Is.True,
                "CorruptionDuPortail.Domain.asmdef must keep noEngineReferences:true — removing it lets the core acquire a UnityEngine/UnityEditor dependency (NFR2 violation).");
        }

        [Test]
        public void Domain_ReferencesNoOtherAssembly()
        {
            var shape = LoadDomainAsmdef();
            var refs = shape.references ?? Array.Empty<string>();
            Assert.That(refs, Is.Empty,
                "CorruptionDuPortail.Domain.asmdef must keep references:[] — the Domain depends on nothing; Game references Domain, never the reverse (NFR2).");
        }
    }
}
