using System.Collections.Generic;
using System.IO;
using Avatars;
using Avatars.Cosmetics;
using CorruptionDuPortail.Domain;
using UI;
using UI.CatCustomization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Avatars.Cosmetics.EditorTools
{
    /// <summary>
    /// Rebuilds the cat cosmetics from the exported art (reproducible, run again after a new export): the materials
    /// of the new parts (copies of the cat's own materials on the CatDitherLit shader, with the Drive's textures), the
    /// material remap of Cat_Cosmetics.fbx, the catalog (ids, names, icons, parts), the CatAppearance on Cat_Avatar
    /// and its wiring on PlayerAvatar, and the customization screen of the main menu.
    ///
    /// Art: Corrupted_Cats.blend (artist's Drive), exported by Claude with Blender (skeleton = the game's BW Rig + the
    /// cape bones of the Fiery rig). Item names are placeholders until the art team names them.
    /// </summary>
    public static class CatCosmeticsAssetBuilder
    {
        private const string MaterialsDir = "Assets/Art/Materials/Cat/Cosmetics";
        private const string TexturesDir = "Assets/Art/Textures/Cat/Cosmetics";
        private const string IconsDir = "Assets/Art/Sprites/CatCosmetics";
        private const string CosmeticsModelPath = "Assets/Art/Models/Cat_Cosmetics.fbx";
        private const string CatalogPath = "Assets/ScriptableObjects/Avatars/CatCosmeticsCatalog.asset";
        private const string CatPrefabPath = "Assets/Prefabs/Avatars/Cat_Avatar.prefab";
        private const string PlayerAvatarPrefabPath = "Assets/Prefabs/Avatars/PlayerAvatar.prefab";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string ScreenUxmlPath = "Assets/UI/Screens/CatCustomization/CatCustomization.uxml";
        private const string RoleBookUxmlPath = "Assets/UI/Screens/RoleBook/RoleBook.uxml";

        private const string BodyMaterialPath = "Assets/Art/Materials/Cat/Black and White Body.mat";
        private const string ClothesMaterialPath = "Assets/Art/Materials/Cat/Cape Red Bow Clothes.mat";
        private const string EyesMaterialPath = "Assets/Art/Materials/Cat/Yellow Eyes Round.mat";

        private static readonly string[] FurRenderers =
            { "Arms", "Arms Fur", "Body", "Body Fur", "Ears", "Ears Fur", "Head", "Head Fur", "Whiskers" };
        private static readonly string[] HairRenderers = { "Arms Fur", "Body Fur", "Ears Fur", "Head Fur" };
        // Replaced by "BW Eyes Expressive" (same eyes + the Angry shape key); shown by CatBlink / CatTears.
        private static readonly string[] HiddenParts = { "BW Eyes Neutral", "BW Eyes Closed", "Tear Emitter" };

        [MenuItem("Tools/Cat Cosmetics/Rebuild assets")]
        public static void RebuildAssets()
        {
            ImportIcons();
            Dictionary<string, Material> materials = BuildMaterials();
            RemapCosmeticsModel(materials);
            CatCosmeticsCatalog catalog = BuildCatalog(materials);
            WireCatPrefab(catalog, materials);
            WirePlayerAvatarPrefab();
            RenderMissingIcons(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[CatCosmetics] Assets rebuilt.");
        }

        [MenuItem("Tools/Cat Cosmetics/Wire main menu")]
        public static void WireMainMenu()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CatCosmeticsCatalog>(CatalogPath);
            var catPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ScreenUxmlPath);
            var roleBookUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RoleBookUxmlPath);

            var scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
            PanelSettings panel = null;
            foreach (UIDocument doc in Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
                if (doc.visualTreeAsset == roleBookUxml) panel = doc.panelSettings;

            GameObject screen = GameObject.Find("CatCustomization") ?? new GameObject("CatCustomization");
            var document = screen.GetComponent<UIDocument>() ?? screen.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = uxml;
            var stage = screen.GetComponent<CatPreviewStage>() ?? screen.AddComponent<CatPreviewStage>();
            SetField(stage, "catModelPrefab", catPrefab);
            var controller = screen.GetComponent<CatCustomizationController>() ?? screen.AddComponent<CatCustomizationController>();
            SetField(controller, "document", document);
            SetField(controller, "catalog", catalog);
            SetField(controller, "stage", stage);

            MainMenu menu = Object.FindAnyObjectByType<MainMenu>(FindObjectsInactive.Include);
            SetField(menu, "catCustomization", controller);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[CatCosmetics] Main menu wired (panel {(panel != null ? panel.name : "MISSING")}).");
        }

        /// <summary>
        /// Check without play mode: dresses the menu's cat stage with each full skin and a few mixes and writes what
        /// its camera sees to Temp/CatLooks/*.png (bind pose: the Animator does not run in edit mode).
        /// </summary>
        [MenuItem("Tools/Cat Cosmetics/Render looks to Temp")]
        public static void RenderLooks()
        {
            var catPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath);
            var go = new GameObject("CatLooksCheck");
            try
            {
                var stage = go.AddComponent<CatPreviewStage>();
                SetField(stage, "catModelPrefab", catPrefab);
                RenderTexture texture = stage.Texture;
                Camera camera = go.GetComponentInChildren<Camera>(true);
                Directory.CreateDirectory("Temp/CatLooks");
                var looks = new Dictionary<string, CatLook>
                {
                    ["default"] = CatLook.Default,
                    ["fiery"] = new CatLook(1, 1, 1, 0),
                    ["galactic"] = new CatLook(2, 2, 2, 1),
                    ["mage"] = new CatLook(3, 3, 3, 2),
                    ["mix"] = new CatLook(0, 1, 3, 2),
                    ["bare"] = new CatLook(2, 4, 0, 0),
                };
                // And every item alone on the default cat.
                var catalog = AssetDatabase.LoadAssetAtPath<CatCosmeticsCatalog>(CatalogPath);
                foreach (CatCosmeticItem item in catalog.items)
                    looks[$"item-{item.slot}-{item.id}"] = catalog.DefaultLook.With(item.slot, item.id);
                foreach (KeyValuePair<string, CatLook> look in looks)
                {
                    stage.Apply(look.Value);
                    camera.Render();
                    RenderTexture.active = texture;
                    var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                    image.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes($"Temp/CatLooks/{look.Key}.png", image.EncodeToPNG());
                    Object.DestroyImmediate(image);
                }
                Debug.Log("[CatCosmetics] Looks rendered to Temp/CatLooks.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------------------------------ steps

        private static void ImportIcons()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { IconsDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer.alphaIsTransparency && importer.mipmapEnabled == false && importer.maxTextureSize == 256) continue;
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, Material> BuildMaterials()
        {
            Directory.CreateDirectory(MaterialsDir);
            var materials = new Dictionary<string, Material>
            {
                ["Black and White Body"] = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath),
                ["Fiery Fur"] = Copy(BodyMaterialPath, "Fiery Fur", "Cat_Fiery_Body", null),
                ["Galactic Fur"] = Copy(BodyMaterialPath, "Galactic Fur", "Cat_Galactic_Body", "Cat_Galactic_Speckles"),
                ["Mage Fur"] = Copy(BodyMaterialPath, "Mage Fur", "Cat_Mage_Body", "Cat_Mage_Speckles"),
                ["Fiery Clothes"] = Copy(ClothesMaterialPath, "Fiery Clothes", "Cat_Fiery_Clothes", null),
                ["Galactic Bow"] = Copy(ClothesMaterialPath, "Galactic Bow", "Cat_Galactic_Bow", null),
                ["Mage Clothes"] = Copy(ClothesMaterialPath, "Mage Clothes", "Cat_Mage_Clothes", null),
                ["Fiery Eyes"] = Copy(EyesMaterialPath, "Fiery Eyes", "Cat_Fiery_Eyes", null),
                ["Galactic Eyes"] = Copy(EyesMaterialPath, "Galactic Eyes", "Cat_Galactic_Eyes", null),
                ["Mage Eyes"] = Copy(EyesMaterialPath, "Mage Eyes", "Cat_Mage_Eyes", null),
                ["Galactic Mouth"] = Mouth(Copy(BodyMaterialPath, "Galactic Mouth", "Cat_Galactic_Mouth", null)),
                ["Mage Mouth"] = Mouth(Copy(BodyMaterialPath, "Mage Mouth", "Cat_Mage_Mouth", null)),
                ["Choked Mouth"] = Mouth(Copy(BodyMaterialPath, "Choked Mouth", "Cat_Choked_Mouth", null)),
                ["Drunk Mouth"] = Mouth(Copy(BodyMaterialPath, "Drunk Mouth", "Cat_Drunk_Mouth", null)),
                ["Sad Mouth"] = Mouth(Copy(BodyMaterialPath, "Sad Mouth", "Cat_Sad_Mouth", null)),
                // Galactic hair cards: the body texture without the speckles (the file's "Galactic Fur").
                ["Galactic Hair"] = Copy(BodyMaterialPath, "Galactic Hair", "Cat_Galactic_Body", null),
                // The file's flat colours (toon shader RGB nodes); the button texture is missing from the Drive.
                ["Black Eyes"] = DoubleSided(Flat(EyesMaterialPath, "Black Eyes", new Color(0.02f, 0.02f, 0.02f))),
                ["Button Eyes"] = DoubleSided(Flat(EyesMaterialPath, "Button Eyes", new Color(0.32f, 0.22f, 0.15f))),
                ["String"] = DoubleSided(Flat(EyesMaterialPath, "String", new Color(0.05f, 0.03f, 0.03f))),
                ["Tear"] = Flat(EyesMaterialPath, "Tear", new Color(0.14f, 0.35f, 1f)),
                // Blink: the round eyes texture darkened (the file mixes it with black).
                ["BW Eyes Closed"] = Tint(Copy(EyesMaterialPath, "BW Eyes Closed", null, null), new Color(0.12f, 0.12f, 0.12f)),
                ["BW Eyes Expressive"] = AssetDatabase.LoadAssetAtPath<Material>(EyesMaterialPath),
            };
            return materials;
        }

        // A copy of one of the cat's materials (same shader, same settings) with another base map; speckles glow
        // through the emission map (they are bright dots on black).
        private static Material Copy(string templatePath, string name, string baseMap, string emissionMap)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>(templatePath)) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.CopyPropertiesFromMaterial(AssetDatabase.LoadAssetAtPath<Material>(templatePath));
            }
            material.SetShaderPassEnabled("MOTIONVECTORS", false); // like the cat's own materials
            if (baseMap != null)
            {
                Texture2D baseTexture = Texture(baseMap);
                material.SetTexture("_BaseMap", baseTexture);
                material.SetTexture("_MainTex", baseTexture);
            }
            if (emissionMap != null)
            {
                material.SetTexture("_EmissionMap", Texture(emissionMap));
                material.SetColor("_EmissionColor", Color.white * 1.4f);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        // A flat colour (no texture) on the cat's shader.
        private static Material Flat(string templatePath, string name, Color color)
        {
            Material material = Copy(templatePath, name, null, null);
            material.SetTexture("_BaseMap", null);
            material.SetTexture("_MainTex", null);
            return Tint(material, color);
        }

        private static Material Tint(Material material, Color color)
        {
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        // Flat cards seen from both sides (the universal eyes are planes).
        private static Material DoubleSided(Material material)
        {
            material.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // The mouths are flat cards keyed out of a blue background (alpha baked in the PNG): cut out, both faces.
        private static Material Mouth(Material material)
        {
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cull", 0f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D Texture(string name) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesDir}/{name}.png")
            ?? throw new FileNotFoundException($"{TexturesDir}/{name}.png");

        private static void RemapCosmeticsModel(Dictionary<string, Material> materials)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(CosmeticsModelPath);
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.importAnimation = false;
            importer.isReadable = true; // the tear emitter's mesh is the tears' particle shape
            importer.importBlendShapes = true; // BW Eyes Expressive: the Angry shape key
            foreach (string name in new[] { "Fiery Clothes", "Galactic Bow", "Mage Clothes", "Fiery Eyes", "Galactic Eyes",
                                             "Mage Eyes", "Galactic Mouth", "Mage Mouth", "Choked Mouth", "Drunk Mouth",
                                             "Sad Mouth", "Black Eyes", "Button Eyes", "String", "Tear", "BW Eyes Closed",
                                             "BW Eyes Expressive" })
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), materials[name]);
            }
            importer.SaveAndReimport();
        }

        private static CatCosmeticsCatalog BuildCatalog(Dictionary<string, Material> materials)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CatCosmeticsCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CatCosmeticsCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.cosmeticsModel = AssetDatabase.LoadAssetAtPath<GameObject>(CosmeticsModelPath);
            catalog.furRenderers = FurRenderers;
            catalog.hairRenderers = HairRenderers;
            catalog.hiddenParts = HiddenParts;
            catalog.defaultFur = catalog.defaultOutfit = catalog.defaultEyes = catalog.defaultMouth = 0;
            catalog.items = new List<CatCosmeticItem>
            {
                Fur(0, "Noir et blanc", "BlackWhite", materials["Black and White Body"]),
                Fur(1, "Ardent", "Fiery", materials["Fiery Fur"]),
                Hair(Fur(2, "Galactique", "Galactic", materials["Galactic Fur"]), materials["Galactic Hair"]),
                Fur(3, "Mage", "Mage", materials["Mage Fur"]),

                Parts(CatSlot.Outfit, 0, "Cape et nœud rouge", "Outfit_BlackWhite", "Robe", "Ribbon", "Bell"),
                Parts(CatSlot.Outfit, 1, "Cape ardente", "Outfit_Fiery",
                    "Fiery Cape", "Fiery Cape Top", "Fiery Fur Neck", "Fiery Medal", "Fiery Metal Decorations"),
                Parts(CatSlot.Outfit, 2, "Nœud galactique", "Outfit_Galactic", "Galactic Bow", "Galactic Bow Mid"),
                Parts(CatSlot.Outfit, 3, "Cape de mage", "Outfit_Mage",
                    "Mage Cape Bottom", "Mage Cape Top", "Mage Cape Star", "Mage Head Star"),
                Parts(CatSlot.Outfit, 4, "Sans tenue", null),

                Blink(Parts(CatSlot.Eyes, 0, "Yeux de la portée", "Eyes_BlackWhite", "BW Eyes Expressive"), "BW Eyes Closed"),
                Parts(CatSlot.Eyes, 1, "Yeux ardents", "Eyes_Fiery", "Fiery Eyes Neutral"),
                Parts(CatSlot.Eyes, 2, "Yeux galactiques", "Eyes_Galactic", "Galactic Eyes", "Galactic Mid"),
                Parts(CatSlot.Eyes, 3, "Yeux de mage", "Eyes_Mage", "Mage Eyes", "Mage Cheek Eyes", "Mage Top Eyes"),
                Parts(CatSlot.Eyes, 4, "Yeux simples", null, "Simple Eyes"),
                Parts(CatSlot.Eyes, 5, "Yeux blasés", null, "Bored Eyes"),
                Parts(CatSlot.Eyes, 6, "Yeux boutons", null, "Full Button Eyes"),

                Parts(CatSlot.Mouth, 0, "Sans bouche", null),
                Parts(CatSlot.Mouth, 1, "Sourire galactique", "Mouth_Galactic", "Galactic Mouth"),
                Parts(CatSlot.Mouth, 2, "Bouche de mage", "Mouth_Mage", "Mage Mouth"),
                Parts(CatSlot.Mouth, 3, "Bouche étouffée", null, "Choked Mouth"),
                Parts(CatSlot.Mouth, 4, "Bouche ivre", null, "Drunk Mouth"),
                Parts(CatSlot.Mouth, 5, "Bouche triste", null, "Sad Mouth"),
            };
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static CatCosmeticItem Fur(byte id, string displayName, string icon, Material material) => new()
        {
            id = id, slot = CatSlot.Fur, displayName = displayName, icon = Icon("Fur_" + icon), furMaterial = material,
        };

        private static CatCosmeticItem Parts(CatSlot slot, byte id, string displayName, string icon, params string[] parts) => new()
        {
            id = id, slot = slot, displayName = displayName, icon = icon != null ? Icon(icon) : null, parts = parts,
        };

        private static CatCosmeticItem Hair(CatCosmeticItem item, Material hair)
        {
            item.hairMaterial = hair;
            return item;
        }

        private static CatCosmeticItem Blink(CatCosmeticItem item, string closedPart)
        {
            item.blinkPart = closedPart;
            return item;
        }

        // Blender-rendered vignettes, else the one RenderMissingIcons wrote for this item (null until then).
        private static Texture2D Icon(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{IconsDir}/CatIcon_{name}.png");

        // ------------------------------------------------------------------------------------------ icons

        // Items without a Blender vignette (parts assembled at export: universal eyes / mouths) get one rendered here:
        // the cat dressed with the item, only the item's parts drawn, framed on them, from the front.
        private static void RenderMissingIcons(CatCosmeticsCatalog catalog)
        {
            var cat = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CatPrefabPath));
            var cameraGo = new GameObject("CatIconCamera");
            var texture = new RenderTexture(256, 240, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            try
            {
                cat.transform.SetPositionAndRotation(new Vector3(0f, -2000f, 0f), Quaternion.Euler(0f, 180f, 0f));
                var appearance = cat.GetComponent<CatAppearance>();
                Camera camera = cameraGo.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.orthographic = true;
                camera.targetTexture = texture;
                var lightGo = new GameObject("Key light");
                lightGo.transform.SetParent(cameraGo.transform, false);
                lightGo.transform.localRotation = Quaternion.Euler(20f, -25f, 0f);
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;

                foreach (CatCosmeticItem item in catalog.items)
                {
                    if (item.slot == CatSlot.Fur || item.parts.Length == 0) continue;
                    string path = $"{IconsDir}/CatIcon_{item.slot}_{item.id}.png";
                    if (item.icon != null && AssetDatabase.GetAssetPath(item.icon) != path) continue; // Blender's
                    appearance.Apply(catalog.DefaultLook.With(item.slot, item.id));
                    var shown = new List<Renderer>();
                    foreach (Renderer r in cat.GetComponentsInChildren<Renderer>(true))
                    {
                        bool mine = System.Array.IndexOf(item.parts, r.name) >= 0;
                        r.enabled = mine;
                        if (mine && r.gameObject.activeInHierarchy) shown.Add(r);
                    }
                    if (shown.Count == 0) continue;
                    Bounds bounds = shown[0].bounds;
                    foreach (Renderer r in shown) bounds.Encapsulate(r.bounds);
                    camera.transform.SetPositionAndRotation(bounds.center - Vector3.forward * 10f, Quaternion.identity);
                    camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x * 240f / 256f) * 1.15f;
                    camera.nearClipPlane = 0.01f;
                    camera.farClipPlane = 50f;
                    camera.Render();
                    RenderTexture.active = texture;
                    var image = new Texture2D(256, 240, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, 256, 240), 0, 0);
                    image.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(path, image.EncodeToPNG());
                    Object.DestroyImmediate(image);
                    AssetDatabase.ImportAsset(path);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.maxTextureSize = 256;
                    importer.SaveAndReimport();
                    item.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                EditorUtility.SetDirty(catalog);
            }
            finally
            {
                Object.DestroyImmediate(cat);
                Object.DestroyImmediate(cameraGo);
                texture.Release();
                Object.DestroyImmediate(texture);
            }
        }

        private static void WireCatPrefab(CatCosmeticsCatalog catalog, Dictionary<string, Material> materials)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CatPrefabPath);
            try
            {
                var appearance = root.GetComponent<CatAppearance>() ?? root.AddComponent<CatAppearance>();
                SetField(appearance, "catalog", catalog);
                if (root.GetComponent<CatBlink>() == null) root.AddComponent<CatBlink>();
                var frown = root.GetComponent<CatFrown>() ?? root.AddComponent<CatFrown>();
                SetField(frown, "animator", root.GetComponent<Animator>());
                var tears = root.GetComponent<CatTears>() ?? root.AddComponent<CatTears>();
                SetField(tears, "animator", root.GetComponent<Animator>());
                SetField(tears, "tearMaterial", materials["Tear"]);
                Mesh tearMesh = null;
                foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(CosmeticsModelPath))
                    if (sub is Mesh mesh && mesh.name == "Tear Particle") tearMesh = mesh;
                SetField(tears, "tearMesh", tearMesh);
                PrefabUtility.SaveAsPrefabAsset(root, CatPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void WirePlayerAvatarPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerAvatarPrefabPath);
            try
            {
                var avatar = root.GetComponent<PlayerAvatar>();
                SetField(avatar, "_catAppearance", root.GetComponentInChildren<CatAppearance>(true));
                PrefabUtility.SaveAsPrefabAsset(root, PlayerAvatarPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private class MissingFieldException : System.Exception
        {
            public MissingFieldException(string type, string field) : base($"{type}.{field} not found") { }
        }
    }
}
