using System.Collections.Generic;
using System.Linq;
using Avatars.Cosmetics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// The cats wear the artist's Blender look: every material the cat and its cosmetics draw with is on
    /// "Corruption/Cat Toon" (keeps the emote dither fade), and the glow masks are read as linear values.
    /// A material left on another shader renders with the old look (reflections, no glow) and no longer fades.
    /// </summary>
    [Category("CatCustomization")]
    public class CatToonLookTests
    {
        private const string ShaderName = "Corruption/Cat Toon";
        private const string CatPrefabPath = "Assets/Prefabs/Avatars/Cat_Avatar.prefab";
        private const string CatalogPath = "Assets/ScriptableObjects/Avatars/CatCosmeticsCatalog.asset";
        private const string MasksDir = "Assets/Art/Textures/Cat/Cosmetics";

        private static IEnumerable<Material> CatMaterials()
        {
            var cat = AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath);
            var catalog = AssetDatabase.LoadAssetAtPath<CatCosmeticsCatalog>(CatalogPath);
            // The hidden parts are never drawn (the tear emitter is only the tears' particle shape).
            IEnumerable<Material> drawn = cat.GetComponentsInChildren<Renderer>(true)
                .Concat(catalog.cosmeticsModel.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => !catalog.hiddenParts.Contains(renderer.name))
                .SelectMany(renderer => renderer.sharedMaterials);
            IEnumerable<Material> furs = catalog.items.SelectMany(item => new[] { item.furMaterial, item.hairMaterial });
            return drawn.Concat(furs).Where(material => material != null).Distinct();
        }

        [Test]
        public void EveryCatMaterial_IsOnTheCatToonShader()
        {
            List<Material> materials = CatMaterials().ToList();
            Assert.That(materials.Count, Is.GreaterThan(10), "the cat's materials were not found");
            string[] others = materials.Where(material => material.shader.name != ShaderName)
                .Select(material => $"{material.name} ({material.shader.name})").ToArray();
            Assert.IsEmpty(others, "cat materials not on " + ShaderName);
            Assert.IsTrue(materials.All(material => material.HasProperty("_CatFadeAmount")));
        }

        [Test]
        public void GlowMasks_AreLinear()
        {
            foreach (string guid in AssetDatabase.FindAssets("Mask t:Texture2D", new[] { MasksDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsFalse(importer.sRGBTexture, path + " holds linear values, it must not be read as sRGB");
            }
        }
    }
}
