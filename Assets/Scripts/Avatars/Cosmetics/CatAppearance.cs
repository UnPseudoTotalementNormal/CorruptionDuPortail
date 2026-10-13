using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using UnityEngine;

namespace Avatars.Cosmetics
{
    /// <summary>
    /// Dresses a cat model (Cat_Avatar, in game and in the menu) from a <see cref="CatLook"/>. On Awake the parts of
    /// the catalog's cosmetics model are bound once onto this cat: its skinned meshes take this skeleton's bones (by
    /// name), its static meshes move under the same-named bone, and the bones this skeleton lacks (the capes') are
    /// grafted under their parent. Then <see cref="Apply"/> only switches parts on and off and puts the fur material.
    ///
    /// Parts are hidden by deactivating their GameObject, never through <c>Renderer.enabled</c>: the avatar visibility
    /// controller owns that flag (first-person body, emote fade) and would turn a hidden part back on.
    /// </summary>
    public class CatAppearance : MonoBehaviour
    {
        private const string SkeletonRootName = "Root";

        [SerializeField] private CatCosmeticsCatalog catalog;

        private readonly Dictionary<string, GameObject> _parts = new();
        private readonly List<Renderer> _furRenderers = new();
        private bool _bound;

        public CatCosmeticsCatalog Catalog => catalog;

        /// <summary>The look on the model (sanitized), default until the first <see cref="Apply"/>.</summary>
        public CatLook Current { get; private set; } = CatLook.Default;

        private void Awake()
        {
            Bind();
            Apply(catalog != null ? catalog.DefaultLook : CatLook.Default);
        }

        /// <summary>Puts <paramref name="look"/> on the cat. Unknown ids fall back to the catalog's defaults.</summary>
        public void Apply(CatLook look)
        {
            Bind();
            if (catalog == null)
            {
                return;
            }
            look = catalog.Sanitize(look);
            Current = look;

            CatCosmeticItem fur = catalog.Find(CatSlot.Fur, look.Fur);
            if (fur != null && fur.furMaterial != null)
            {
                foreach (Renderer renderer in _furRenderers)
                {
                    renderer.sharedMaterial = fur.furMaterial;
                }
            }

            foreach (CatSlot slot in CatLook.Slots)
            {
                if (slot == CatSlot.Fur)
                {
                    continue;
                }
                CatCosmeticItem chosen = catalog.Find(slot, look.Get(slot));
                // Every part of the slot off, then the chosen item's on (two items may share a part).
                foreach (CatCosmeticItem item in catalog.ItemsOf(slot))
                {
                    if (item != chosen)
                    {
                        SetParts(item, false);
                    }
                }
                if (chosen != null)
                {
                    SetParts(chosen, true);
                }
            }
        }

        private void SetParts(CatCosmeticItem item, bool on)
        {
            foreach (string part in item.parts)
            {
                if (_parts.TryGetValue(part, out GameObject go) && go.activeSelf != on)
                {
                    go.SetActive(on);
                }
            }
        }

        // ------------------------------------------------------------------------------------------ binding

        private void Bind()
        {
            if (_bound || catalog == null)
            {
                return;
            }
            _bound = true;

            Transform skeleton = FindDeep(transform, SkeletonRootName);
            if (skeleton == null)
            {
                Debug.LogError($"[CatAppearance] No '{SkeletonRootName}' bone under {name}: cosmetics not bound.", this);
                return;
            }
            Dictionary<string, Transform> bones = BonesUnder(skeleton);

            var furNames = new HashSet<string>(catalog.furRenderers);
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                _parts[renderer.name] = renderer.gameObject;
                if (furNames.Contains(renderer.name))
                {
                    _furRenderers.Add(renderer);
                }
            }

            if (catalog.cosmeticsModel != null)
            {
                GraftCosmetics(bones);
            }
        }

        private void GraftCosmetics(Dictionary<string, Transform> bones)
        {
            GameObject source = Instantiate(catalog.cosmeticsModel, transform);
            source.name = catalog.cosmeticsModel.name + " (source)";
            Transform sourceSkeleton = FindDeep(source.transform, SkeletonRootName);
            if (sourceSkeleton == null)
            {
                Debug.LogError($"[CatAppearance] The cosmetics model has no '{SkeletonRootName}' bone.", this);
                DestroyObject(source);
                return;
            }

            // Listed first: grafting a cape bone carries away the parts hanging on it (medal, star).
            Renderer[] sourceParts = source.GetComponentsInChildren<Renderer>(true);

            // 1) Bones this cat lacks (capes): move the top-most missing bone of each chain under its parent, its whole
            // chain (and the parts hanging on it) comes along, local pose unchanged.
            foreach (Transform bone in BonesUnder(sourceSkeleton).Values)
            {
                if (bones.ContainsKey(bone.name) || bone.parent == null || !bones.TryGetValue(bone.parent.name, out Transform parent))
                {
                    continue;
                }
                bone.SetParent(parent, false);
                foreach (KeyValuePair<string, Transform> grafted in BonesUnder(bone))
                {
                    bones[grafted.Key] = grafted.Value;
                }
            }

            // 2) The parts: skinned meshes take this skeleton, static ones hang on the same-named bone.
            foreach (Renderer renderer in sourceParts)
            {
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    Transform[] sourceBones = skinned.bones;
                    var mapped = new Transform[sourceBones.Length];
                    for (var i = 0; i < sourceBones.Length; i++)
                    {
                        if (sourceBones[i] == null || !bones.TryGetValue(sourceBones[i].name, out mapped[i]))
                        {
                            Debug.LogError($"[CatAppearance] Bone '{sourceBones[i]?.name}' of '{skinned.name}' not on the cat.", this);
                        }
                    }
                    skinned.bones = mapped;
                    if (skinned.rootBone != null && bones.TryGetValue(skinned.rootBone.name, out Transform root))
                    {
                        skinned.rootBone = root;
                    }
                    skinned.transform.SetParent(transform, false);
                }
                else if (renderer.transform.parent != null && !HasRenderer(renderer.transform.parent)
                         && bones.TryGetValue(renderer.transform.parent.name, out Transform bone))
                {
                    renderer.transform.SetParent(bone, false);
                }
                _parts[renderer.name] = renderer.gameObject;
                renderer.gameObject.SetActive(false);
            }

            DestroyObject(source);
        }

        // Gone at once, not at the end of the frame: the cat's other components (idle motion, head look) scan its
        // bones by name in their own Awake, right after this one, and would cache the source's duplicate skeleton.
        private static void DestroyObject(GameObject target)
        {
            target.transform.SetParent(null, false);
            DestroyImmediate(target);
        }

        // Bones = transforms without a renderer ("Head" is both a bone and the mesh hanging on it).
        private static Dictionary<string, Transform> BonesUnder(Transform root)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!HasRenderer(t) && !bones.ContainsKey(t.name))
                {
                    bones[t.name] = t;
                }
            }
            return bones;
        }

        private static bool HasRenderer(Transform t) => t.GetComponent<Renderer>() != null;

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name && !HasRenderer(t))
                {
                    return t;
                }
            }
            return null;
        }
    }
}
