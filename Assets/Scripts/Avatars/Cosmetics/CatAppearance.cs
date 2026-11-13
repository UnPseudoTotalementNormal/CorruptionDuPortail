using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using UnityEngine;

namespace Avatars.Cosmetics
{
    /// <summary>
    /// Dresses a cat model (Cat_Avatar, in game and in the menu) from a <see cref="CatLook"/>. On Awake the parts of
    /// the catalog's cosmetics model are bound once onto this cat: its skinned meshes take the weights of this cat's own
    /// skin (FollowBody), its static meshes move under the same-named bone, and the bones this skeleton lacks (the
    /// capes') are grafted under their parent. Then <see cref="Apply"/> only switches parts on and off and puts the fur material.
    ///
    /// Parts are hidden by deactivating their GameObject, never through <c>Renderer.enabled</c>: the avatar visibility
    /// controller owns that flag (first-person body, emote fade) and would turn a hidden part back on.
    /// </summary>
    public class CatAppearance : MonoBehaviour
    {
        private const string SkeletonRootName = "Root";
        // The cat's own skin, whose weights the clothes take (FollowBody): the body for every piece, the arms only for a
        // piece worn on the arms (sleeves), else a torso piece next to an arm (the apron's ties) left with the paw.
        private const string BodySkinName = "Body";
        private const string ArmsSkinName = "Arms";
        // Skin vertices blended for each vertex of a piece of clothing.
        private const int SkinNeighbours = 4;

        [SerializeField] private CatCosmeticsCatalog catalog;

        private readonly Dictionary<string, GameObject> _parts = new();
        private readonly List<Renderer> _furRenderers = new();
        private readonly HashSet<Renderer> _hairRenderers = new();
        private readonly Dictionary<string, Material> _partMaterials = new();
        private bool _bound;
        // Meshes copied to follow the body (FollowBody), this cat's own: destroyed with it.
        private readonly List<Mesh> _rebound = new();
        private bool _blinking;
        private bool _mouthHidden;
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
                Diagnostics.Logs.GameLog.Warn("avatar", "avatar.cosmetics.no-catalog", "name", name);
                return;
            }
            CatLook _requested = look;
            look = catalog.Sanitize(look);
            if (look.Pack() != _requested.Pack())
            {
                Diagnostics.Logs.GameLog.Warn("avatar", "avatar.cosmetics.fallback", "name", name, "requested", _requested.Encode(), "applied", look.Encode());
            }
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
                    WearPartMaterial(chosen);
                    SetParts(chosen, slot != CatSlot.Mouth || !_mouthHidden);
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

        /// <summary>Hides / shows the worn mouth (CatTalk: the talking mouth takes its place while the cat speaks).</summary>
        public void SetMouthHidden(bool hidden)
        {
            if (hidden == _mouthHidden) return;
            _mouthHidden = hidden;
            CatCosmeticItem mouth = catalog != null ? catalog.Find(CatSlot.Mouth, Current.Mouth) : null;
            if (mouth != null) SetParts(mouth, !hidden);
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

        // Two items may share a mesh with different textures: the part wears the chosen item's material, or its own back.
        private void WearPartMaterial(CatCosmeticItem item)
        {
            foreach (string part in item.parts)
            {
                if (!_parts.TryGetValue(part, out GameObject go) || !go.TryGetComponent(out Renderer renderer)) continue;
                if (!_partMaterials.TryGetValue(part, out Material own))
                {
                    own = renderer.sharedMaterial;
                    _partMaterials[part] = own;
                }
                renderer.sharedMaterial = item.partMaterial != null ? item.partMaterial : own;
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
            List<SkinnedMeshRenderer> skin = FindSkin(source.transform);
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
                    FollowBody(skinned, skin, bones);
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

        // The cat's own skin (not the cosmetics model's): its body, and its arms after it.
        private List<SkinnedMeshRenderer> FindSkin(Transform source)
        {
            var skin = new List<SkinnedMeshRenderer>();
            foreach (string skinName in new[] { BodySkinName, ArmsSkinName })
            {
                foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (r.name == skinName && !r.transform.IsChildOf(source) && r.sharedMesh != null)
                    {
                        skin.Add(r);
                        break;
                    }
                }
            }
            return skin;
        }

        // A piece worn on the arms: every vertex of it hangs mostly on an arm bone (the chef's sleeves).
        private static bool WornOnTheArms(SkinnedMeshRenderer part)
        {
            Transform[] bones = part.bones;
            foreach (BoneWeight w in part.sharedMesh.boneWeights)
            {
                if (w.boneIndex0 < 0 || w.boneIndex0 >= bones.Length || bones[w.boneIndex0] == null) return false;
                string bone = bones[w.boneIndex0].name;
                if (!bone.StartsWith("Arm") && !bone.StartsWith("Hand")) return false;
            }
            return true;
        }

        // How a cosmetics mesh follows the cat's skin: the same for every cat (same model, same rest pose), computed
        // once per mesh. Bone weights index BoneNames.
        private sealed class SkinFit
        {
            public string[] BoneNames;
            public BoneWeight[] Weights;
        }

        private static readonly Dictionary<Mesh, SkinFit> Fits = new();

        // The clothes of the cosmetics model do not follow the body by their own weights: the capes, the chef's apron,
        // shirt and collar, the galactic bow ride the cape bones of the artist's rig (Cape_MCH…, driven by constraints in
        // Blender, never by the game's animations) or the skeleton's root, and the chef's sleeves were hung rigidly on
        // the nearest arm bone at export. The body bent under them and came out of them in every emote (Wouh
        // 2026-11-11, T3) or stayed still in the chained crush (galactic bow, Poyo 2026-10-22). Every vertex takes the
        // bones of the nearest points of the cat's skin, blended by distance, so the cloth deforms exactly like the
        // body under it; the mesh is bound again on the pose it is drawn in now (the rest pose, in Awake), so nothing
        // moves on the spot. A copy of the mesh, this cat's own.
        private void FollowBody(SkinnedMeshRenderer part, List<SkinnedMeshRenderer> skin, Dictionary<string, Transform> bones)
        {
            Mesh mesh = part.sharedMesh;
            if (mesh == null || skin.Count == 0 || mesh.blendShapeCount > 0 || mesh.boneWeights.Length != mesh.vertexCount)
            {
                return;
            }

            // The part as drawn now. Baked without scale: the skeleton's own scale is already in the vertices; only the
            // renderer's place and turn are left to apply.
            var baked = new Mesh();
            part.BakeMesh(baked, false);
            Vector3[] partVertices = baked.vertices;
            Vector3[] partNormals = baked.normals;
            Vector4[] partTangents = baked.tangents;
            Transform pt = part.transform;

            if (!Fits.TryGetValue(mesh, out SkinFit fit))
            {
                bool arms = WornOnTheArms(part);
                fit = FitToSkin(part, partVertices, arms ? skin : skin.FindAll(r => r.name == BodySkinName), baked);
                Fits[mesh] = fit;
                Diagnostics.Logs.GameLog.Info("avatar", "avatar.cosmetics.fit-to-skin", "part", part.name,
                    "vertices", partVertices.Length, "arms", arms, "bones", fit != null ? string.Join("+", fit.BoneNames) : "none");
            }
            if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
            if (fit == null)
            {
                return;
            }

            var partBones = new Transform[fit.BoneNames.Length];
            for (int b = 0; b < partBones.Length; b++)
            {
                if (!bones.TryGetValue(fit.BoneNames[b], out partBones[b]))
                {
                    Debug.LogError($"[CatAppearance] Bone '{fit.BoneNames[b]}' of the skin not on the cat: '{part.name}' keeps its own weights.", this);
                    return;
                }
            }

            // Bound on the pose drawn now: each bone's bind is where it stands, the vertices where they are drawn.
            Vector3 scale = pt.lossyScale;
            var vertices = new Vector3[partVertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector3(partVertices[i].x / scale.x, partVertices[i].y / scale.y, partVertices[i].z / scale.z);
            }
            var bindposes = new Matrix4x4[partBones.Length];
            for (int b = 0; b < partBones.Length; b++)
            {
                bindposes[b] = partBones[b].worldToLocalMatrix * pt.localToWorldMatrix;
            }
            Mesh copy = Instantiate(mesh);
            copy.name = mesh.name + " (follows the body)";
            copy.vertices = vertices;
            if (partNormals.Length == vertices.Length) copy.normals = partNormals;
            if (partTangents.Length == vertices.Length) copy.tangents = partTangents;
            copy.boneWeights = fit.Weights;
            copy.bindposes = bindposes;
            copy.RecalculateBounds();
            part.bones = partBones;
            part.sharedMesh = copy;
            _rebound.Add(copy);
        }

        // Each vertex of the part: the bone weights of its SkinNeighbours nearest skin vertices, blended by inverse
        // squared distance, the 4 heaviest kept. Positions in the cat's own space, so the fit holds for every cat.
        private SkinFit FitToSkin(SkinnedMeshRenderer part, Vector3[] partVertices, List<SkinnedMeshRenderer> skin, Mesh baked)
        {
            var skinPoints = new List<Vector3>();
            var skinWeights = new List<BoneWeight>();
            var skinBones = new List<Transform[]>();
            var skinOf = new List<int>();
            for (int s = 0; s < skin.Count; s++)
            {
                SkinnedMeshRenderer r = skin[s];
                r.BakeMesh(baked, false);
                Vector3[] vs = baked.vertices;
                BoneWeight[] ws = r.sharedMesh.boneWeights;
                if (ws.Length != vs.Length) continue;
                Transform st = r.transform;
                for (int j = 0; j < vs.Length; j++)
                {
                    skinPoints.Add(transform.InverseTransformPoint(st.position + st.rotation * vs[j]));
                    skinWeights.Add(ws[j]);
                    skinOf.Add(skinBones.Count);
                }
                skinBones.Add(r.bones);
            }
            if (skinPoints.Count < SkinNeighbours)
            {
                return null;
            }

            var names = new List<string>();
            var index = new Dictionary<string, int>();
            int IndexOf(Transform bone)
            {
                if (!index.TryGetValue(bone.name, out int i))
                {
                    i = names.Count;
                    names.Add(bone.name);
                    index[bone.name] = i;
                }
                return i;
            }

            var weights = new BoneWeight[partVertices.Length];
            var nearest = new int[SkinNeighbours];
            var distances = new float[SkinNeighbours];
            var sum = new Dictionary<int, float>();
            Transform pt = part.transform;
            for (int v = 0; v < partVertices.Length; v++)
            {
                Vector3 at = transform.InverseTransformPoint(pt.position + pt.rotation * partVertices[v]);
                for (int k = 0; k < SkinNeighbours; k++) { nearest[k] = -1; distances[k] = float.MaxValue; }
                for (int j = 0; j < skinPoints.Count; j++)
                {
                    float d = (skinPoints[j] - at).sqrMagnitude;
                    if (d >= distances[SkinNeighbours - 1]) continue;
                    int k = SkinNeighbours - 1;
                    while (k > 0 && distances[k - 1] > d)
                    {
                        distances[k] = distances[k - 1];
                        nearest[k] = nearest[k - 1];
                        k--;
                    }
                    distances[k] = d;
                    nearest[k] = j;
                }

                sum.Clear();
                for (int k = 0; k < SkinNeighbours; k++)
                {
                    if (nearest[k] < 0) continue;
                    float share = 1f / (distances[k] + 1e-12f);
                    BoneWeight w = skinWeights[nearest[k]];
                    Transform[] sb = skinBones[skinOf[nearest[k]]];
                    void Add(int bone, float weight)
                    {
                        if (weight <= 0f || bone < 0 || bone >= sb.Length || sb[bone] == null) return;
                        int i = IndexOf(sb[bone]);
                        sum[i] = (sum.TryGetValue(i, out float x) ? x : 0f) + weight * share;
                    }
                    Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1);
                    Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
                }
                weights[v] = Heaviest(sum);
            }
            return new SkinFit { BoneNames = names.ToArray(), Weights = weights };
        }

        // The 4 heaviest bones of a blend, normalized, heaviest first (Unity's order).
        private static BoneWeight Heaviest(Dictionary<int, float> sum)
        {
            var top = new List<KeyValuePair<int, float>>(sum);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            float total = 0f;
            for (int k = 0; k < top.Count && k < 4; k++) total += top[k].Value;
            var w = new BoneWeight();
            if (total <= 0f) return w;
            if (top.Count > 0) { w.boneIndex0 = top[0].Key; w.weight0 = top[0].Value / total; }
            if (top.Count > 1) { w.boneIndex1 = top[1].Key; w.weight1 = top[1].Value / total; }
            if (top.Count > 2) { w.boneIndex2 = top[2].Key; w.weight2 = top[2].Value / total; }
            if (top.Count > 3) { w.boneIndex3 = top[3].Key; w.weight3 = top[3].Value / total; }
            return w;
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
