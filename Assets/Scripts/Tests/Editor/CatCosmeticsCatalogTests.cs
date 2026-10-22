using System.Collections.Generic;
using System.Linq;
using Avatars;
using Avatars.Cosmetics;
using CorruptionDuPortail.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// The authored cat cosmetics: the catalog is coherent (stable ids, defaults, names, icons), every part it names
    /// exists, and dressing the real Cat_Avatar shows exactly the chosen parts, skinned on the cat's own skeleton.
    /// </summary>
    [Category("CatCustomization")]
    public class CatCosmeticsCatalogTests
    {
        private const string CatalogPath = "Assets/ScriptableObjects/Avatars/CatCosmeticsCatalog.asset";
        private const string CatPrefabPath = "Assets/Prefabs/Avatars/Cat_Avatar.prefab";
        private const string PlayerAvatarPrefabPath = "Assets/Prefabs/Avatars/PlayerAvatar.prefab";

        private static CatCosmeticsCatalog Catalog => AssetDatabase.LoadAssetAtPath<CatCosmeticsCatalog>(CatalogPath);

        private GameObject _cat;

        [TearDown]
        public void TearDown()
        {
            if (_cat != null) Object.DestroyImmediate(_cat);
        }

        [Test]
        public void Catalog_HasEveryPart_UniqueIds_AndItsDefaults()
        {
            CatCosmeticsCatalog catalog = Catalog;
            Assert.IsNotNull(catalog, CatalogPath);
            foreach (CatSlot slot in CatLook.Slots)
            {
                List<CatCosmeticItem> items = catalog.ItemsOf(slot);
                Assert.IsNotEmpty(items, $"{slot}: no cosmetic");
                Assert.AreEqual(items.Count, items.Select(i => i.id).Distinct().Count(), $"{slot}: duplicate id");
                Assert.IsTrue(catalog.Contains(slot, catalog.DefaultLook.Get(slot)), $"{slot}: default id not in the catalog");
            }
        }

        [Test]
        public void Items_HaveAName_AnIcon_AndTheirContent()
        {
            foreach (CatCosmeticItem item in Catalog.items)
            {
                string label = $"{item.slot} {item.id}";
                Assert.IsFalse(string.IsNullOrWhiteSpace(item.displayName), label);
                StringAssert.DoesNotContain("—", item.displayName, $"{label}: no em dash in player text");
                if (item.slot == CatSlot.Fur)
                {
                    Assert.IsNotNull(item.furMaterial, $"{label}: fur without material");
                    Assert.IsNotNull(item.icon, $"{label}: no vignette");
                }
                else if (item.parts.Length > 0)
                {
                    Assert.IsNotNull(item.icon, $"{label}: no vignette");
                }
            }
        }

        [Test]
        public void EveryNamedPart_ExistsOnTheCatOrInTheCosmeticsModel()
        {
            CatCosmeticsCatalog catalog = Catalog;
            var names = new HashSet<string>();
            foreach (Renderer r in AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath).GetComponentsInChildren<Renderer>(true))
                names.Add(r.name);
            var furNames = new HashSet<string>(names);
            foreach (Renderer r in catalog.cosmeticsModel.GetComponentsInChildren<Renderer>(true))
                names.Add(r.name);

            foreach (string fur in catalog.furRenderers)
                Assert.IsTrue(furNames.Contains(fur), $"fur renderer '{fur}' not on Cat_Avatar");
            foreach (CatCosmeticItem item in catalog.items)
                foreach (string part in item.parts)
                    Assert.IsTrue(names.Contains(part), $"{item.slot} {item.id}: part '{part}' found nowhere");
        }

        [Test]
        public void EveryCosmetic_FollowsTheBody_NotTheSkeletonRootAlone()
        {
            // The galactic bow was exported 71 % on the root bone: it went along with the cat but stayed still while the
            // body leaned or was crushed (Poyo 2026-10-22). Bound, every skinned part rides the body's bones.
            CatCosmeticsCatalog catalog = Catalog;
            _cat = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath));
            var appearance = _cat.GetComponent<CatAppearance>();
            appearance.Apply(CatLook.Default); // edit mode: no Awake, Apply binds
            Dictionary<string, Renderer> renderers = _cat.GetComponentsInChildren<Renderer>(true)
                .GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First());

            int checkedParts = 0;
            foreach (CatCosmeticItem item in catalog.items.Where(i => i.slot != CatSlot.Fur))
                foreach (string part in item.parts)
                {
                    if (!renderers.TryGetValue(part, out Renderer renderer) || !(renderer is SkinnedMeshRenderer skinned)
                        || skinned.bones.Length == 0 || skinned.sharedMesh == null || skinned.sharedMesh.blendShapeCount > 0)
                        continue;
                    int root = System.Array.FindIndex(skinned.bones, b => b != null && b.name == "Root");
                    BoneWeight[] weights = skinned.sharedMesh.boneWeights;
                    int onRoot = weights.Count(w => w.boneIndex0 == root && w.weight0 > 0.5f);
                    Assert.Less(onRoot, 0.01f * weights.Length + 1f, $"'{part}': {onRoot}/{weights.Length} vertices on the root bone alone");
                    checkedParts++;
                }
            Assert.Greater(checkedParts, 0, "no skinned cosmetic checked");
        }

        [Test]
        public void Apply_ShowsExactlyTheChosenParts_SkinnedOnTheCatSkeleton()
        {
            CatCosmeticsCatalog catalog = Catalog;
            _cat = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath));
            var appearance = _cat.GetComponent<CatAppearance>();
            Assert.IsNotNull(appearance, "Cat_Avatar has no CatAppearance");
            appearance.Apply(CatLook.Default); // edit mode: no Awake, Apply binds

            Assert.IsNull(_cat.transform.Find(catalog.cosmeticsModel.name + " (source)"), "the cosmetics source was not cleaned up");
            // One skeleton only: the cat's components scan bones by name (AvatarIdleMotion cached the source's
            // duplicate Spine bones, destroyed at the end of the frame: NRE every frame in game, 2026-10-13).
            foreach (string bone in new[] { "Root", "Spine1", "Head", "Ear1.L", "Cape Master" })
                Assert.AreEqual(1, _cat.GetComponentsInChildren<Transform>(true).Count(t => t.name == bone && t.GetComponent<Renderer>() == null),
                    $"bone '{bone}' is not unique on the dressed cat");
            Dictionary<string, Renderer> renderers = _cat.GetComponentsInChildren<Renderer>(true)
                .GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First());
            var catTransforms = new HashSet<Transform>(_cat.GetComponentsInChildren<Transform>(true));

            foreach (CatCosmeticItem chosen in catalog.items.Where(i => i.slot != CatSlot.Fur))
            {
                appearance.Apply(CatLook.Default.With(chosen.slot, chosen.id));
                foreach (CatCosmeticItem other in catalog.ItemsOf(chosen.slot))
                    foreach (string part in other.parts)
                    {
                        Assert.IsTrue(renderers.ContainsKey(part), $"part '{part}' not bound on the cat");
                        bool shouldShow = chosen.parts.Contains(part);
                        Assert.AreEqual(shouldShow, renderers[part].gameObject.activeInHierarchy,
                            $"{chosen.slot} {chosen.id} '{chosen.displayName}': part '{part}'");
                        if (renderers[part] is SkinnedMeshRenderer skinned)
                            Assert.IsTrue(skinned.bones.All(b => b != null && catTransforms.Contains(b)),
                                $"'{part}' is not skinned on the cat's own bones");
                    }
            }

            foreach (CatCosmeticItem fur in catalog.ItemsOf(CatSlot.Fur))
            {
                appearance.Apply(CatLook.Default.With(CatSlot.Fur, fur.id));
                foreach (string name in catalog.furRenderers)
                {
                    Material expected = fur.hairMaterial != null && catalog.hairRenderers.Contains(name)
                        ? fur.hairMaterial : fur.furMaterial;
                    Assert.AreSame(expected, renderers[name].sharedMaterial, $"fur {fur.id} on '{name}'");
                }
            }
        }

        [Test]
        public void HiddenParts_StayHidden_TheBlackAndWhiteEyesBlink_AndKeepTheirAngryShape()
        {
            CatCosmeticsCatalog catalog = Catalog;
            _cat = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath));
            var appearance = _cat.GetComponent<CatAppearance>();
            appearance.Apply(CatLook.Default);
            foreach (string hidden in catalog.hiddenParts)
            {
                Assert.IsNotNull(appearance.Part(hidden), $"hidden part '{hidden}' not on the cat");
                Assert.IsFalse(appearance.Part(hidden).activeInHierarchy, $"'{hidden}' shown");
            }

            CatCosmeticItem eyes = catalog.Find(CatSlot.Eyes, catalog.DefaultLook.Eyes);
            Assert.IsTrue(appearance.CanBlink, "the default eyes have no closed variant");
            appearance.SetBlink(true);
            Assert.IsTrue(appearance.Part(eyes.blinkPart).activeInHierarchy, "closed eyes not shown while blinking");
            Assert.IsFalse(appearance.Part(eyes.parts[0]).activeInHierarchy, "open eyes still shown while blinking");
            appearance.SetBlink(false);
            Assert.IsFalse(appearance.Part(eyes.blinkPart).activeInHierarchy);
            Assert.IsTrue(appearance.Part(eyes.parts[0]).activeInHierarchy);

            var skinned = appearance.Part(eyes.parts[0]).GetComponent<SkinnedMeshRenderer>();
            Assert.IsNotNull(skinned, "the expressive eyes are not a skinned mesh (blend shapes)");
            int angry = skinned.sharedMesh.GetBlendShapeIndex("Angry");
            Assert.GreaterOrEqual(angry, 0, "the Angry shape key was lost");
            appearance.SetAngry(1f);
            Assert.AreEqual(100f, skinned.GetBlendShapeWeight(angry), 0.01f);

            appearance.Apply(CatLook.Default.With(CatSlot.Eyes, 4));
            Assert.IsFalse(appearance.CanBlink, "eyes without a closed variant must not blink");
        }

        [Test]
        public void Apply_UnknownIds_FallBackToTheDefaults()
        {
            _cat = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath));
            var appearance = _cat.GetComponent<CatAppearance>();
            appearance.Apply(new CatLook(250, 250, 250, 250));
            Assert.AreEqual(Catalog.DefaultLook, appearance.Current);
        }

        [Test]
        public void PlayerAvatar_DressesItsNestedCat()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerAvatarPrefabPath);
            var avatar = prefab.GetComponent<PlayerAvatar>();
            var wired = new SerializedObject(avatar).FindProperty("_catAppearance").objectReferenceValue as CatAppearance;
            Assert.IsNotNull(wired, "PlayerAvatar._catAppearance not wired");
            Assert.AreSame(prefab.GetComponentInChildren<CatAppearance>(true), wired);
        }
    }
}
