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
        private const string SkinName = "Body";
        // A cosmetic with at least this share of its vertices on the skeleton's root alone does not follow the body.
        private const float LooseShare = 0.01f;

        [SerializeField] private CatCosmeticsCatalog catalog;

        private readonly Dictionary<string, GameObject> _parts = new();
        private readonly List<Renderer> _furRenderers = new();
        private readonly HashSet<Renderer> _hairRenderers = new();
        private bool _bound;
        // Meshes copied to follow the body (FollowBody), this cat's own: destroyed with it.
        private readonly List<Mesh> _rebound = new();
        private bool _blinking;
        private float _angry;

        public CatCosmeticsCatalog Catalog => catalog;

        /// <summary>The look on the model (sanitized), default until the first <see cref="Apply"/>.</summary>
        public CatLook Current { get; private set; } = CatLook.Default;

        private void Awake()
        {
            Bind();
            Apply(catalog != null ? catalog.DefaultLook : CatLook.Default);
        }

        private void OnDestroy()
        {
            foreach (Mesh mesh in _rebound)
            {
                if (mesh != null) Destroy(mesh);
            }
            _rebound.Clear();
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
                    renderer.sharedMaterial = fur.hairMaterial != null && _hairRenderers.Contains(renderer)
                        ? fur.hairMaterial : fur.furMaterial;
                }
            }
            foreach (string hidden in catalog.hiddenParts)
            {
                if (_parts.TryGetValue(hidden, out GameObject go) && go.activeSelf) go.SetActive(false);
            }
            _blinking = false;

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
            SetAngry(_angry);
        }

        // ------------------------------------------------------------------------------------------ expressions

        /// <summary>The part named <paramref name="part"/> once bound (null if the cat has none).</summary>
        public GameObject Part(string part)
        {
            Bind();
            return _parts.TryGetValue(part, out GameObject go) ? go : null;
        }

        /// <summary>True when the worn eyes can blink (their item names a closed-eyes part).</summary>
        public bool CanBlink => BlinkItem() != null;

        /// <summary>Closes / opens the worn eyes (CatBlink). No-op for eyes without a closed variant.</summary>
        public void SetBlink(bool closed)
        {
            CatCosmeticItem eyes = BlinkItem();
            if (eyes == null || closed == _blinking) return;
            _blinking = closed;
            SetParts(eyes, !closed);
            if (_parts.TryGetValue(eyes.blinkPart, out GameObject lids)) lids.SetActive(closed);
        }

        /// <summary>Weight 0..1 of the eyes' "Angry" shape (the black and white eyes have one), for later use.</summary>
        public void SetAngry(float weight)
        {
            _angry = Mathf.Clamp01(weight);
            if (catalog == null) return;
            CatCosmeticItem eyes = catalog.Find(CatSlot.Eyes, Current.Eyes);
            if (eyes == null) return;
            foreach (string part in eyes.parts)
            {
                if (!_parts.TryGetValue(part, out GameObject go) || !go.TryGetComponent(out SkinnedMeshRenderer skinned)
                    || skinned.sharedMesh == null) continue;
                int index = skinned.sharedMesh.GetBlendShapeIndex("Angry");
                if (index >= 0) skinned.SetBlendShapeWeight(index, _angry * 100f);
            }
        }

        private CatCosmeticItem BlinkItem()
        {
            if (catalog == null) return null;
            CatCosmeticItem eyes = catalog.Find(CatSlot.Eyes, Current.Eyes);
            return eyes != null && !string.IsNullOrEmpty(eyes.blinkPart) && _parts.ContainsKey(eyes.blinkPart) ? eyes : null;
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
            var hairNames = new HashSet<string>(catalog.hairRenderers);
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                _parts[renderer.name] = renderer.gameObject;
                if (furNames.Contains(renderer.name))
                {
                    _furRenderers.Add(renderer);
                    if (hairNames.Contains(renderer.name)) _hairRenderers.Add(renderer);
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
            SkinnedMeshRenderer skin = FindSkin(source.transform);
            bones.TryGetValue(SkeletonRootName, out Transform skeletonRoot);
            foreach (Renderer renderer in sourceParts)
            {
                // A mesh with blend shapes only (no bones) imports as skinned too: it hangs on its bone like a static one.
                if (renderer is SkinnedMeshRenderer skinned && skinned.bones.Length > 0)
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
                    FollowBody(skinned, skin, skeletonRoot);
                }
                else if (renderer.transform.parent != null && !HasRenderer(renderer.transform.parent)
                         && bones.TryGetValue(renderer.transform.parent.name, out Transform bone))
                {
                    renderer.transform.SetParent(bone, false);
                }
                // A mesh hanging on nothing of the skeleton (the tear card: a particle mesh) is not a part of the cat.
                if (renderer.transform.IsChildOf(source.transform))
                {
                    continue;
                }
                _parts[renderer.name] = renderer.gameObject;
                renderer.gameObject.SetActive(false);
            }

            DestroyObject(source);
        }

        // The cat's own body mesh (not the cosmetics model's).
        private SkinnedMeshRenderer FindSkin(Transform source)
        {
            foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.name == SkinName && !r.transform.IsChildOf(source)) return r;
            }
            return null;
        }

        // Some cosmetics were exported with vertices on the skeleton's root alone (the galactic bow: 71 % of them): they
        // went along with the cat but not with its body (leaning, the chained crush, Poyo 2026-10-22). Those vertices take
        // the bones of the nearest point of the cat's skin, and the mesh is bound again on the pose it is drawn in now, so
        // nothing moves on the spot. A copy of the mesh, this cat's own.
        private void FollowBody(SkinnedMeshRenderer part, SkinnedMeshRenderer skin, Transform skeletonRoot)
        {
            Mesh mesh = part.sharedMesh;
            if (mesh == null || skin == null || skin.sharedMesh == null || skeletonRoot == null || mesh.blendShapeCount > 0)
            {
                return;
            }
            Transform[] bones = part.bones;
            int root = System.Array.IndexOf(bones, skeletonRoot);
            BoneWeight[] weights = mesh.boneWeights;
            if (root < 0 || weights.Length != mesh.vertexCount)
            {
                return;
            }
            var loose = new List<int>();
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i].boneIndex0 == root && weights[i].weight0 > 0.5f) loose.Add(i);
            }
            if (loose.Count == 0 || loose.Count < LooseShare * weights.Length)
            {
                return;
            }

            // Both meshes as drawn now. Baked without scale: the skeleton's own scale is already in the vertices; only
            // the renderer's place and turn are left to apply.
            var baked = new Mesh();
            part.BakeMesh(baked, false);
            Vector3[] partVertices = baked.vertices;
            Vector3[] partNormals = baked.normals;
            Vector4[] partTangents = baked.tangents;
            skin.BakeMesh(baked, false);
            Vector3[] skinVertices = baked.vertices;
            if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
            BoneWeight[] skinWeights = skin.sharedMesh.boneWeights;
            Transform[] skinBones = skin.bones;
            Transform pt = part.transform, st = skin.transform;
            var skinWorld = new Vector3[skinVertices.Length];
            for (int j = 0; j < skinVertices.Length; j++) skinWorld[j] = st.position + st.rotation * skinVertices[j];
            var boneIndex = new Dictionary<Transform, int>();
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] != null && !boneIndex.ContainsKey(bones[b])) boneIndex[bones[b]] = b;
            }

            bool Map(int skinBone, out int partBone)
            {
                partBone = 0;
                return skinBone >= 0 && skinBone < skinBones.Length && skinBones[skinBone] != null
                    && boneIndex.TryGetValue(skinBones[skinBone], out partBone);
            }

            int moved = 0;
            foreach (int i in loose)
            {
                Vector3 at = pt.position + pt.rotation * partVertices[i];
                int nearest = -1;
                float best = float.MaxValue;
                for (int j = 0; j < skinWorld.Length && j < skinWeights.Length; j++)
                {
                    float d = (skinWorld[j] - at).sqrMagnitude;
                    if (d < best) { best = d; nearest = j; }
                }
                if (nearest < 0) continue;
                BoneWeight w = skinWeights[nearest];
                if (!Map(w.boneIndex0, out int b0) || !Map(w.boneIndex1, out int b1) || !Map(w.boneIndex2, out int b2)
                    || !Map(w.boneIndex3, out int b3))
                {
                    continue;
                }
                w.boneIndex0 = b0; w.boneIndex1 = b1; w.boneIndex2 = b2; w.boneIndex3 = b3;
                weights[i] = w;
                moved++;
            }
            if (moved == 0)
            {
                return;
            }

            // Bound on the pose drawn now: each bone's bind is where it stands, the vertices where they are drawn.
            Vector3 scale = pt.lossyScale;
            var vertices = new Vector3[partVertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector3(partVertices[i].x / scale.x, partVertices[i].y / scale.y, partVertices[i].z / scale.z);
            }
            Matrix4x4[] old = mesh.bindposes;
            var bindposes = new Matrix4x4[bones.Length];
            for (int b = 0; b < bones.Length; b++)
            {
                bindposes[b] = bones[b] != null ? bones[b].worldToLocalMatrix * pt.localToWorldMatrix
                    : (b < old.Length ? old[b] : Matrix4x4.identity);
            }
            Mesh copy = Instantiate(mesh);
            copy.name = mesh.name + " (follows the body)";
            copy.vertices = vertices;
            if (partNormals.Length == vertices.Length) copy.normals = partNormals;
            if (partTangents.Length == vertices.Length) copy.tangents = partTangents;
            copy.boneWeights = weights;
            copy.bindposes = bindposes;
            copy.RecalculateBounds();
            part.sharedMesh = copy;
            _rebound.Add(copy);
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
