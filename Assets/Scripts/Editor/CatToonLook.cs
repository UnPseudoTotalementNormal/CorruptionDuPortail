using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Avatars.Cosmetics.EditorTools
{
    /// <summary>
    /// Puts every cat material on "Corruption/Cat Toon" with the values of the artist's Blender materials
    /// (Corrupted_Cats.blend, read node by node): the share of the flat two-tone, its shadow value, and the glowing parts
    /// (masks baked by tools/art/cats/bake_emission_masks.py). Textures, alpha clip and culling stay as they are.
    /// Run by Rebuild assets; on its own after a change of these values.
    /// </summary>
    public static class CatToonLook
    {
        public const string ShaderName = "Corruption/Cat Toon";
        private const string MaterialsDir = "Assets/Art/Materials/Cat";
        private const string MasksDir = "Assets/Art/Textures/Cat/Cosmetics";

        /// <summary>How much the flat half of the toon ignores the lights: 1 = as in Blender (the cat reads in the dark), 0 =
        /// lit by the game only, an unlit cat keeps only its glowing parts (chosen on the mockups, chats-shader v1 : B).</summary>
        public const float SelfLight = 0f;

        // Blender's colours (Emission node colour × strength), linear.
        private static readonly Color BlackWhiteDots = new Color(1f, 0.155f, 0.002f) * 3f;
        private static readonly Color GalacticCracks = new Color(1f, 0.755f, 0.434f);
        private static readonly Color Speckles = new Color(1f, 0.237f, 0.019f) * 3f;
        private static readonly Color GlowingEyes = new Color(1f, 0.316f, 0.001f) * 11.2f;

        private sealed class Glow
        {
            public string Mask;
            public Color Colour;
            public float Cover = 1f;
            public float CoverBias;
            public bool ScaleByMask;
        }

        private sealed class Look
        {
            public float ToonMix = 0.5f;
            public float Shadow = 0.5f;
            public Glow Glow1;
            public Glow Glow2;
        }

        // Plain toon: Toon Shader+ mixed half and half with a Diffuse BSDF, shadow at half value.
        private static readonly Look Toon = new Look();
        // The mouths are their texture plugged straight into the output: flat, the same whatever the light.
        private static readonly Look Flat = new Look { ToonMix = 1f, Shadow = 1f };
        // Button eyes and the button string: a Principled BSDF alone (Mix Shader factor 0).
        private static readonly Look Diffuse = new Look { ToonMix = 0f };
        // Fiery / galactic eyes: Mix Shader 0.5 towards an Emission whose strength is the eyes texture × 11.2.
        private static Look GlowingEyesLook(string mask) => new Look
        {
            Glow1 = new Glow { Mask = mask, Colour = GlowingEyes, Cover = 0f, CoverBias = 0.5f, ScaleByMask = true },
        };

        private static readonly Dictionary<string, Look> Looks = new Dictionary<string, Look>
        {
            ["Black and White Body"] = new Look { Glow1 = new Glow { Mask = "Cat_BlackWhite_GlowMask", Colour = BlackWhiteDots } },
            ["Cape Red Bow Clothes"] = Toon,
            ["Yellow Eyes Round"] = Toon,
            ["Yellow Eyes"] = Toon,
            ["Cosmetics/BW Eyes Closed"] = Toon,
            ["Cosmetics/Fiery Fur"] = Toon,
            // Unity's "Galactic Fur" dresses the body (Blender's "Galactic Body"), "Galactic Hair" the fur cards.
            ["Cosmetics/Galactic Fur"] = new Look
            {
                Glow1 = new Glow { Mask = "Cat_Galactic_CracksMask", Colour = GalacticCracks },
                Glow2 = new Glow { Mask = "Cat_Galactic_SpecklesMask", Colour = Speckles },
            },
            ["Cosmetics/Galactic Hair"] = Toon,
            ["Cosmetics/Mage Fur"] = new Look { Glow1 = new Glow { Mask = "Cat_Mage_SpecklesMask", Colour = Speckles } },
            ["Cosmetics/Fiery Clothes"] = Toon,
            ["Cosmetics/Galactic Bow"] = Toon,
            ["Cosmetics/Mage Clothes"] = Toon,
            ["Cosmetics/Chef Clothes"] = Toon,
            ["Cosmetics/Fiery Eyes"] = GlowingEyesLook("Cat_Fiery_EyesMask"),
            ["Cosmetics/Galactic Eyes"] = GlowingEyesLook("Cat_Galactic_EyesMask"),
            ["Cosmetics/Mage Eyes"] = Toon,
            ["Cosmetics/Black Eyes"] = Toon,
            ["Cosmetics/Button Eyes"] = Diffuse,
            ["Cosmetics/String"] = Diffuse,
            ["Cosmetics/Tear"] = Toon,
            ["Cosmetics/Galactic Mouth"] = Flat,
            ["Cosmetics/Mage Mouth"] = Flat,
            ["Cosmetics/Choked Mouth"] = Flat,
            ["Cosmetics/Drunk Mouth"] = Flat,
            ["Cosmetics/Sad Mouth"] = Flat,
            ["Cosmetics/Talking Mouth"] = Flat,
        };

        /// <summary>Every material this look covers (asset paths), for the tests.</summary>
        public static IEnumerable<string> MaterialPaths
        {
            get
            {
                foreach (string name in Looks.Keys) yield return $"{MaterialsDir}/{name}.mat";
            }
        }

        [MenuItem("Tools/Cat Cosmetics/Apply toon look")]
        public static void Apply()
        {
            Shader shader = Shader.Find(ShaderName) ?? throw new FileNotFoundException(ShaderName);
            foreach (KeyValuePair<string, Look> entry in Looks)
            {
                string path = $"{MaterialsDir}/{entry.Key}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path) ?? throw new FileNotFoundException(path);
                Apply(material, shader, entry.Value);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[CatCosmetics] Toon look applied to {Looks.Count} materials.");
        }

        private static void Apply(Material material, Shader shader, Look look)
        {
            int queue = material.renderQueue;
            material.shader = shader; // properties carry over by name (_BaseMap, _BaseColor, _Cutoff, _Cull…)
            material.renderQueue = queue;
            // Lit's keywords mean nothing here; keep the two this shader reads.
            bool alphaClip = material.GetFloat("_AlphaClip") > 0.5f;
            bool receiveShadows = material.GetFloat("_ReceiveShadows") > 0.5f;
            material.shaderKeywords = new string[0];
            if (alphaClip) material.EnableKeyword("_ALPHATEST_ON");
            if (!receiveShadows) material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            material.SetFloat("_ToonMix", look.ToonMix);
            material.SetFloat("_ToonShadow", look.Shadow);
            material.SetFloat("_ToonSelfLight", SelfLight);
            SetGlow(material, "_Emission", look.Glow1);
            SetGlow(material, "_Emission2", look.Glow2);
            material.SetShaderPassEnabled("MOTIONVECTORS", false); // like the cat's materials until now
            EditorUtility.SetDirty(material);
        }

        private static void SetGlow(Material material, string prefix, Glow glow)
        {
            material.SetTexture($"{prefix}Map", glow == null ? null : Mask(glow.Mask));
            material.SetColor($"{prefix}Color", glow?.Colour ?? Color.black);
            material.SetFloat($"{prefix}Cover", glow?.Cover ?? 1f);
            material.SetFloat($"{prefix}CoverBias", glow?.CoverBias ?? 0f);
            material.SetFloat($"{prefix}ScaleByMask", glow != null && glow.ScaleByMask ? 1f : 0f);
        }

        // The masks hold linear values (Blender's luminance of the texture): never read as sRGB.
        private static Texture2D Mask(string name)
        {
            string path = $"{MasksDir}/{name}.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new FileNotFoundException(path);
            if (importer.sRGBTexture || importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
