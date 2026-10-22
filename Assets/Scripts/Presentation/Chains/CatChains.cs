using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Presentation.Chains
{
    /// <summary>
    /// The chains of a chained cat (maquettes chaines v1 O3 + T1 + M2 + R1, v2 A1 + P1, Poyo 2026-10-22): they appear on
    /// it link by link, first around its right paw then its left one (the artist's clip raises the right arm first), then
    /// around its body, while the force lifts it; they stay on it for the rest of the game, and crushed on the floor it
    /// keeps them (they flatten with it, nothing spreads on the floor).
    /// <para>Built once, from the cat's own shape in its idle pose (its baked meshes), then skinned on its skeleton: every
    /// link follows the bone of the body point it lies on, so the chains take every pose the cat takes, at no cost per
    /// frame. The renderer exists from the start (empty), so <c>AvatarVisibilityController</c> hides it with the body.</para>
    /// <para>Client-side visual only. <c>ChainRevealDirector</c> plays the appearing; otherwise the Animator's synced
    /// <c>Chained</c> bool decides (a player joining a game sees the chained cats chained at once).</para>
    /// </summary>
    public sealed class CatChains : MonoBehaviour
    {
        private const string ChainedParam = "Chained";
        private const string IdleClip = "Idle";

        // The appearing, in seconds into the chained clips (ChainRevealTimeline.ClipTime): the right arm goes up at 1.0,
        // the left one at 1.5, both are held out from 2.2 (measured on « Getting Chained » / « Getting Chained 2 »).
        private const float RightArmFrom = 0.85f, RightArmTo = 1.15f;
        private const float LeftArmFrom = 1.35f, LeftArmTo = 1.65f;
        private const float BodyFrom = 1.65f, BodyTo = 2.15f;

        [SerializeField] private Material _material;
        [Tooltip("Height of the cat model at scale 1, ears included (the chains' size follows it).")]
        [SerializeField] private float _catHeightAtScale1 = 4.4f;
        [Tooltip("A cat chained while watched, with no reveal played for it (a missed moment), gets its chains this late.")]
        [SerializeField] private float _fallbackSeconds = 15f;

        private enum State { None, Revealing, Full }

        private struct Piece
        {
            public int FirstLink;
            public int LinkCount;
            public float From;
            public float To;
        }

        private Animator _animator;
        private SkinnedMeshRenderer _renderer;
        private Mesh _mesh;
        private Mesh _baked; // scratch for the body's baked shape
        private int[] _allIndices;
        private readonly List<Piece> _pieces = new();
        private readonly List<int> _shown = new();
        private State _state = State.None;
        private bool _built;
        private bool _seen;
        private bool _wasChained;
        private float _chainedSince;

        /// <summary>True while its chains are drawn, in part or whole.</summary>
        public bool Shown => _state != State.None;

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>(true);
            SkinnedMeshRenderer _body = FindBody();
            if (_body == null)
            {
                return;
            }
            var _go = new GameObject("Chains");
            _go.layer = _body.gameObject.layer;
            _go.transform.SetParent(_body.transform.parent, false);
            _renderer = _go.AddComponent<SkinnedMeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.renderingLayerMask = _body.renderingLayerMask;
            _renderer.updateWhenOffscreen = true;
            _renderer.rootBone = _body.rootBone;
            _mesh = new Mesh { name = "CatChains", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _renderer.sharedMesh = _mesh;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }
            if (_baked != null)
            {
                Destroy(_baked);
            }
        }

        /// <summary>The moment starts: the chains are drawn by <see cref="SetRevealTime"/> until <see cref="EndReveal"/>.</summary>
        public void BeginReveal()
        {
            _state = State.Revealing;
            Show(0f);
        }

        /// <summary>
        /// Called after the chained pose of this frame is sampled (<paramref name="_clipTime"/> into the chained clips):
        /// the links that have appeared by then are drawn.
        /// </summary>
        public void SetRevealTime(float _clipTime)
        {
            if (_state == State.Revealing)
            {
                Show(_clipTime);
            }
        }

        /// <summary>The moment is over (or was cut short): the cat keeps all its chains.</summary>
        public void EndReveal() => ShowFull();

        public void ShowFull()
        {
            _state = State.Full;
            Show(float.MaxValue);
        }

        public void Clear()
        {
            _state = State.None;
            if (_mesh != null && _built)
            {
                _mesh.SetTriangles(System.Array.Empty<int>(), 0, false);
            }
        }

        private void LateUpdate()
        {
            if (_animator == null || _renderer == null)
            {
                return;
            }
            bool _chained = _animator.GetBool(ChainedParam);
            if (!_seen)
            {
                // Chained already when we first see it (joining a game): chained for good, no moment to wait for.
                _seen = true;
                _wasChained = _chained;
                if (_chained)
                {
                    ShowFull();
                }
                return;
            }
            if (_chained && !_wasChained)
            {
                _chainedSince = Time.unscaledTime;
            }
            // Unchained (the bool falls back): no more chains. Not while the moment plays: the server's bool may come
            // a little after it starts.
            if (!_chained && _wasChained && _state != State.Revealing)
            {
                Clear();
            }
            _wasChained = _chained;
            if (_chained && _state == State.None && Time.unscaledTime - _chainedSince > _fallbackSeconds)
            {
                ShowFull();
            }
        }

        // Draws the links of each piece that have appeared by _clipTime (one after the other along the piece).
        private void Show(float _clipTime)
        {
            if (!_built && !Build())
            {
                return;
            }
            _shown.Clear();
            foreach (Piece _piece in _pieces)
            {
                float _share = _clipTime >= _piece.To ? 1f : Mathf.Clamp01((_clipTime - _piece.From) / (_piece.To - _piece.From));
                int _count = Mathf.CeilToInt(_share * _piece.LinkCount - 1e-4f);
                int _start = _piece.FirstLink * ChainGeometry.IndicesPerLink;
                _shown.AddRange(new System.ArraySegment<int>(_allIndices, _start, _count * ChainGeometry.IndicesPerLink));
            }
            _mesh.SetTriangles(_shown, 0, false);
        }

        private SkinnedMeshRenderer FindBody()
        {
            foreach (SkinnedMeshRenderer _r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (_r.name == "Body")
                {
                    return _r;
                }
            }
            return null;
        }

        private static bool IsHead(string _name) => _name.Contains("Ear") || _name.Contains("Head") || _name.Contains("Eye") || _name.Contains("Whisker");
        private static bool IsArm(string _name) => _name.StartsWith("Arms");
        private const string BodySkin = "Body";
        private const string ArmsSkin = "Arms";

        // A baked body point: where it is now, and the bones that move it.
        private struct Point
        {
            public Vector3 Position;
            public BoneWeight Weight;
        }

        // ---- the build: the cat's shape in its idle pose, the chains laid on it, skinned on its skeleton
        private bool Build()
        {
            SkinnedMeshRenderer _body = FindBody();
            if (_body == null || _renderer == null || _animator == null)
            {
                return false;
            }
            Transform[] _bones = _body.bones;
            var _boneIndex = new Dictionary<Transform, int>();
            for (int _i = 0; _i < _bones.Length; _i++)
            {
                if (_bones[_i] != null) _boneIndex[_bones[_i]] = _i;
            }

            // The idle pose's first frame: the pose is overwritten again this frame (the Animator, the reveal's sampling).
            if (_animator.runtimeAnimatorController != null)
            {
                foreach (AnimationClip _clip in _animator.runtimeAnimatorController.animationClips)
                {
                    if (_clip != null && _clip.name == IdleClip)
                    {
                        _clip.SampleAnimation(_animator.gameObject, 0f);
                        break;
                    }
                }
            }

            var _bodyPoints = new List<Point>();   // torso and clothes: the wrap's shape, its links' bones
            var _armPoints = new List<Point>();    // the arms: the cuffs' shape and bones
            float _bottom = float.MaxValue, _bodyMax = float.MinValue, _headBottom = float.MaxValue;
            if (_baked == null) _baked = new Mesh { name = "CatChainsBake" };
            foreach (SkinnedMeshRenderer _r in GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                // (parts are hidden by deactivation, CatAppearance; enabled is the visibility's, off for one's own body)
                if (_r == _renderer || _r.sharedMesh == null)
                {
                    continue;
                }
                string _name = _r.name;
                _r.BakeMesh(_baked, false);
                // Baked without scale: the skeleton's own scale is already in the vertices; only the renderer's place
                // and turn are left to apply (its scale would count twice).
                Transform _t = _r.transform;
                Vector3[] _vs = _baked.vertices;
                BoneWeight[] _ws = _r.sharedMesh.boneWeights;
                Transform[] _rb = _r.bones;
                bool _head = IsHead(_name), _arm = IsArm(_name);
                // Only the cat's own skin lends its bones: the fur shells copy it, and the clothes may ride a bigger
                // skeleton of their own (the capes: 75 bones, Cape_MCH...) that the body's does not have.
                bool _skin = _name == BodySkin || _name == ArmsSkin;
                for (int _i = 0; _i < _vs.Length; _i++)
                {
                    Vector3 _p = _t.position + _t.rotation * _vs[_i];
                    if (_head)
                    {
                        if (_name.StartsWith("Ears")) _headBottom = Mathf.Min(_headBottom, _p.y);
                        continue;
                    }
                    var _point = new Point { Position = _p, Weight = _skin && _i < _ws.Length ? Remap(_ws[_i], _rb, _boneIndex) : default };
                    if (!_skin) _point.Weight.weight0 = -1f; // shape only
                    if (_arm) _armPoints.Add(_point);
                    else
                    {
                        _bodyPoints.Add(_point);
                        _bottom = Mathf.Min(_bottom, _p.y);
                        _bodyMax = Mathf.Max(_bodyMax, _p.y);
                    }
                }
            }
            if (_bodyPoints.Count == 0)
            {
                return false;
            }

            float _catHeight = _catHeightAtScale1 * transform.lossyScale.y;
            ChainGeometry.LinkSize _size = ChainGeometry.LinkSize.ForCatHeight(_catHeight);
            // the body ends where the head begins (its big head comes down over the neck)
            float _bodyTop = Mathf.Max(Mathf.Min(_bodyMax, _headBottom + 0.04f * _catHeight), _bottom + 0.1f * _catHeight);
            var _shape = new BodyShape(_bodyPoints, _bottom, _bodyTop, _size.WireRadius * 1.6f);

            // The cat faces the way its eyes look.
            Vector3 _facing = transform.forward;
            Transform _eyes = Find("Eyes"), _headBone = Find("Head");
            if (_eyes != null && _headBone != null)
            {
                Vector3 _look = _eyes.position - _headBone.position;
                _look.y = 0f;
                if (_look.sqrMagnitude > 1e-6f) _facing = _look.normalized;
            }
            float _facingAngle = Mathf.Atan2(_facing.z, _facing.x);

            var _links = new List<ChainGeometry.LinkFrame>();
            var _linkWeights = new List<BoneWeight>();
            _pieces.Clear();

            // Its right paw, then its left one: a few turns at the end of each arm, like cuffs (v2 A1).
            AddCuff(new[] { Find("Arm1.R"), Find("Arm2.R"), Find("Hand.R") }, new[] { Find("Arm1.L"), Find("Arm2.L"), Find("Hand.L") },
                _armPoints, _size, _catHeight, RightArmFrom, RightArmTo, _links, _linkWeights);
            AddCuff(new[] { Find("Arm1.L"), Find("Arm2.L"), Find("Hand.L") }, new[] { Find("Arm1.R"), Find("Arm2.R"), Find("Hand.R") },
                _armPoints, _size, _catHeight, LeftArmFrom, LeftArmTo, _links, _linkWeights);

            // Then its body: a spiral from its belly to under its head, and one turn round its legs.
            int _bodyFirst = _links.Count;
            ChainGeometry.LayAlong(_shape.Helix(0.22f, 0.86f, 2.4f, _facingAngle + Mathf.PI + 2.2f), _shape.Outward, _size, _links);
            ChainGeometry.LayAlong(_shape.Helix(0.06f, 0.18f, 1f, _facingAngle + Mathf.PI - 0.6f), _shape.Outward, _size, _links);
            for (int _l = _bodyFirst; _l < _links.Count; _l++)
            {
                _linkWeights.Add(Nearest(_bodyPoints, _links[_l].Centre));
            }
            _pieces.Add(new Piece { FirstLink = _bodyFirst, LinkCount = _links.Count - _bodyFirst, From = BodyFrom, To = BodyTo });

            // The mesh, in the chains' own space, bound to the skeleton as it stands now.
            var _vertices = new List<Vector3>(_links.Count * ChainGeometry.VerticesPerLink);
            var _normals = new List<Vector3>(_vertices.Capacity);
            var _indices = new List<int>(_links.Count * ChainGeometry.IndicesPerLink);
            var _weights = new List<BoneWeight>(_vertices.Capacity);
            Transform _space = _renderer.transform;
            for (int _l = 0; _l < _links.Count; _l++)
            {
                int _from = _vertices.Count;
                ChainGeometry.AddLink(_links[_l], _size, _vertices, _normals, _indices);
                for (int _v = _from; _v < _vertices.Count; _v++)
                {
                    _vertices[_v] = _space.InverseTransformPoint(_vertices[_v]);
                    _normals[_v] = _space.InverseTransformDirection(_normals[_v]);
                    _weights.Add(_linkWeights[_l]);
                }
            }
            var _bindposes = new Matrix4x4[_bones.Length];
            for (int _i = 0; _i < _bones.Length; _i++)
            {
                _bindposes[_i] = _bones[_i] != null ? _bones[_i].worldToLocalMatrix * _space.localToWorldMatrix : Matrix4x4.identity;
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetNormals(_normals);
            _mesh.boneWeights = _weights.ToArray();
            _mesh.bindposes = _bindposes;
            _allIndices = _indices.ToArray();
            _mesh.SetTriangles(System.Array.Empty<int>(), 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
            _renderer.bones = _bones;
            _built = true;
            if (_state != State.Revealing)
            {
                _animator.Update(0f); // back to the Animator's own pose this frame (the idle one was only for the shape)
            }
            Debug.Log($"[CHAINS] {name}: {_links.Count} links built");
            return true;
        }

        private void AddCuff(Transform[] _arm, Transform[] _otherArm, List<Point> _armPoints, ChainGeometry.LinkSize _size, float _catHeight,
            float _from, float _to, List<ChainGeometry.LinkFrame> _links, List<BoneWeight> _linkWeights)
        {
            if (System.Array.IndexOf(_arm, null) >= 0 || System.Array.IndexOf(_otherArm, null) >= 0 || _armPoints.Count == 0)
            {
                return;
            }
            var _limb = new Limb(_arm, _otherArm, _armPoints, _catHeight, _size.WireRadius * 1.6f);
            int _first = _links.Count;
            ChainGeometry.LayAlong(_limb.Helix(0.62f, 0.86f, 2.6f), _limb.Outward, _size, _links);
            var _ownPoints = _limb.OwnPoints;
            for (int _l = _first; _l < _links.Count; _l++)
            {
                _linkWeights.Add(Nearest(_ownPoints, _links[_l].Centre));
            }
            _pieces.Add(new Piece { FirstLink = _first, LinkCount = _links.Count - _first, From = _from, To = _to });
        }

        private Transform Find(string _bone)
        {
            foreach (Transform _t in GetComponentsInChildren<Transform>(true))
            {
                if (_t.name == _bone) return _t;
            }
            return null;
        }

        // The weights of the nearest point of the skin (the body, or the arms for the cuffs): the link moves with it.
        private static BoneWeight Nearest(List<Point> _points, Vector3 _at)
        {
            float _best = float.MaxValue;
            BoneWeight _weight = default;
            _weight.weight0 = 1f;
            foreach (Point _p in _points)
            {
                if (_p.Weight.weight0 < 0f) continue;
                float _d = (_p.Position - _at).sqrMagnitude;
                if (_d < _best)
                {
                    _best = _d;
                    _weight = _p.Weight;
                }
            }
            return _weight;
        }

        // A renderer's weights, its bone indices turned into the body's (the meshes share the skeleton, not its order).
        private static BoneWeight Remap(BoneWeight _w, Transform[] _rendererBones, Dictionary<Transform, int> _bodyIndex)
        {
            int Map(int _i) => _i >= 0 && _i < _rendererBones.Length && _rendererBones[_i] != null && _bodyIndex.TryGetValue(_rendererBones[_i], out int _j) ? _j : 0;
            _w.boneIndex0 = Map(_w.boneIndex0);
            _w.boneIndex1 = Map(_w.boneIndex1);
            _w.boneIndex2 = Map(_w.boneIndex2);
            _w.boneIndex3 = Map(_w.boneIndex3);
            return _w;
        }

        // ---- the body's shape: how far it reaches in each direction, slice by slice (a support function)
        private sealed class BodyShape
        {
            private const int Slices = 16, Angles = 48;
            private static readonly float[] Cos = new float[Angles], Sin = new float[Angles];
            private readonly float[,] _reach = new float[Slices, Angles];

            static BodyShape()
            {
                for (int _a = 0; _a < Angles; _a++)
                {
                    Cos[_a] = Mathf.Cos(_a * Mathf.PI * 2f / Angles);
                    Sin[_a] = Mathf.Sin(_a * Mathf.PI * 2f / Angles);
                }
            }
            private readonly Vector3[] _centres = new Vector3[Slices];
            private readonly float _bottom, _top, _clearance;

            public BodyShape(List<Point> _points, float _bottom, float _top, float _clearance)
            {
                this._bottom = _bottom;
                this._top = Mathf.Max(_top, _bottom + 1e-3f);
                this._clearance = _clearance;
                float _slice = (this._top - _bottom) / Slices;
                var _min = new Vector2[Slices];
                var _max = new Vector2[Slices];
                for (int _s = 0; _s < Slices; _s++)
                {
                    _min[_s] = new Vector2(float.MaxValue, float.MaxValue);
                    _max[_s] = new Vector2(float.MinValue, float.MinValue);
                }
                // each point counts for its slice and the two next to it (smoother)
                foreach (Point _p in _points)
                {
                    int _s0 = Mathf.FloorToInt((_p.Position.y - _bottom) / _slice);
                    for (int _s = _s0 - 1; _s <= _s0 + 1; _s++)
                    {
                        if (_s < 0 || _s >= Slices) continue;
                        _min[_s] = Vector2.Min(_min[_s], new Vector2(_p.Position.x, _p.Position.z));
                        _max[_s] = Vector2.Max(_max[_s], new Vector2(_p.Position.x, _p.Position.z));
                    }
                }
                for (int _s = 0; _s < Slices; _s++)
                {
                    Vector2 _c = _min[_s].x <= _max[_s].x ? (_min[_s] + _max[_s]) * 0.5f : Vector2.zero;
                    _centres[_s] = new Vector3(_c.x, _bottom + (_s + 0.5f) * _slice, _c.y);
                }
                foreach (Point _p in _points)
                {
                    int _s0 = Mathf.FloorToInt((_p.Position.y - _bottom) / _slice);
                    for (int _s = _s0 - 1; _s <= _s0 + 1; _s++)
                    {
                        if (_s < 0 || _s >= Slices) continue;
                        float _dx = _p.Position.x - _centres[_s].x, _dz = _p.Position.z - _centres[_s].z;
                        for (int _a = 0; _a < Angles; _a++)
                        {
                            float _d = _dx * Cos[_a] + _dz * Sin[_a];
                            if (_d > _reach[_s, _a]) _reach[_s, _a] = _d;
                        }
                    }
                }
            }

            /// <summary>The point on the body's surface at height share <paramref name="_h"/>, toward <paramref name="_angle"/>.</summary>
            public Vector3 At(float _h, float _angle)
            {
                float _fs = Mathf.Clamp(_h * Slices - 0.5f, 0f, Slices - 1.001f);
                int _s0 = (int)_fs;
                float _sf = _fs - _s0;
                int _s1 = Mathf.Min(_s0 + 1, Slices - 1);
                float _fa = Mathf.Repeat(_angle / (Mathf.PI * 2f), 1f) * Angles;
                int _a0 = (int)_fa % Angles, _a1 = (_a0 + 1) % Angles;
                float _af = _fa - (int)_fa;
                float _r0 = Mathf.Lerp(_reach[_s0, _a0], _reach[_s0, _a1], _af), _r1 = Mathf.Lerp(_reach[_s1, _a0], _reach[_s1, _a1], _af);
                Vector3 _c = Vector3.Lerp(_centres[_s0], _centres[_s1], _sf);
                float _r = Mathf.Lerp(_r0, _r1, _sf) + _clearance;
                return new Vector3(_c.x + Mathf.Cos(_angle) * _r, Mathf.Lerp(_bottom, _top, _h), _c.z + Mathf.Sin(_angle) * _r);
            }

            public List<Vector3> Helix(float _h0, float _h1, float _turns, float _angle0)
            {
                const int Steps = 360;
                var _path = new List<Vector3>(Steps + 1);
                for (int _i = 0; _i <= Steps; _i++)
                {
                    float _q = _i / (float)Steps;
                    _path.Add(At(Mathf.Lerp(_h0, _h1, _q), _angle0 + _q * _turns * Mathf.PI * 2f));
                }
                return _path;
            }

            public Vector3 Outward(Vector3 _p)
            {
                float _fs = Mathf.Clamp((_p.y - _bottom) / (_top - _bottom) * Slices - 0.5f, 0f, Slices - 1f);
                Vector3 _d = _p - _centres[(int)_fs];
                _d.y = 0f;
                return _d.sqrMagnitude > 1e-8f ? _d.normalized : Vector3.forward;
            }
        }

        // ---- an arm: its bones' line (shoulder, elbow, paw, the paw's end) and how thick it is along it
        private sealed class Limb
        {
            private const int Bins = 8;
            private readonly Vector3[] _axis;
            private readonly float[] _radius = new float[Bins];
            private readonly float _length;
            private readonly float _clearance;
            public readonly List<Point> OwnPoints = new();

            public Limb(Transform[] _bones, Transform[] _otherBones, List<Point> _armPoints, float _catHeight, float _clearance)
            {
                _axis = AxisOf(_bones);
                Vector3[] _other = AxisOf(_otherBones);
                this._clearance = _clearance;
                for (int _i = 1; _i < _axis.Length; _i++) _length += Vector3.Distance(_axis[_i - 1], _axis[_i]);
                var _bins = new List<float>[Bins];
                for (int _b = 0; _b < Bins; _b++) _bins[_b] = new List<float>();
                foreach (Point _p in _armPoints)
                {
                    Closest(_axis, _p.Position, out Vector3 _c, out float _u);
                    Closest(_other, _p.Position, out Vector3 _co, out _);
                    float _d = (_p.Position - _c).magnitude;
                    if (_d > (_p.Position - _co).magnitude || _d > 0.16f * _catHeight) continue;
                    OwnPoints.Add(_p);
                    _bins[Mathf.Min(Bins - 1, (int)(_u * Bins))].Add(_d);
                }
                for (int _b = 0; _b < Bins; _b++)
                {
                    List<float> _l = _bins[_b];
                    _l.Sort();
                    _radius[_b] = _l.Count > 4 ? _l[(int)(_l.Count * 0.85f)] : 0.05f * _catHeight;
                }
            }

            private static Vector3[] AxisOf(Transform[] _b)
            {
                Vector3 _paw = _b[2].position;
                return new[] { _b[0].position, _b[1].position, _paw, _paw + (_paw - _b[1].position) * 0.45f };
            }

            private static void Closest(Vector3[] _axis, Vector3 _p, out Vector3 _point, out float _u)
            {
                float _total = 0f;
                for (int _i = 1; _i < _axis.Length; _i++) _total += Vector3.Distance(_axis[_i - 1], _axis[_i]);
                float _best = float.MaxValue, _along = 0f;
                _point = _axis[0];
                _u = 0f;
                for (int _i = 1; _i < _axis.Length; _i++)
                {
                    Vector3 _d = _axis[_i] - _axis[_i - 1];
                    float _len = _d.magnitude;
                    float _q = _len > 1e-6f ? Mathf.Clamp01(Vector3.Dot(_p - _axis[_i - 1], _d) / (_len * _len)) : 0f;
                    Vector3 _c = _axis[_i - 1] + _d * _q;
                    float _dd = (_p - _c).sqrMagnitude;
                    if (_dd < _best)
                    {
                        _best = _dd;
                        _point = _c;
                        _u = _total > 1e-6f ? (_along + _q * _len) / _total : 0f;
                    }
                    _along += _len;
                }
            }

            private Vector3 Along(float _u)
            {
                float _want = _u * _length, _acc = 0f;
                for (int _i = 1; _i < _axis.Length; _i++)
                {
                    float _len = Vector3.Distance(_axis[_i - 1], _axis[_i]);
                    if (_acc + _len >= _want) return Vector3.Lerp(_axis[_i - 1], _axis[_i], _len > 1e-6f ? (_want - _acc) / _len : 0f);
                    _acc += _len;
                }
                return _axis[_axis.Length - 1];
            }

            public List<Vector3> Helix(float _u0, float _u1, float _turns)
            {
                const int Steps = 240;
                var _path = new List<Vector3>(Steps + 1);
                for (int _i = 0; _i <= Steps; _i++)
                {
                    float _q = _i / (float)Steps;
                    float _u = Mathf.Lerp(_u0, _u1, _q);
                    Vector3 _c = Along(_u);
                    Vector3 _d = (Along(Mathf.Min(1f, _u + 0.02f)) - Along(Mathf.Max(0f, _u - 0.02f))).normalized;
                    Vector3 _e1 = Vector3.Cross(_d, Vector3.up);
                    if (_e1.sqrMagnitude < 1e-6f) _e1 = Vector3.Cross(_d, Vector3.forward);
                    _e1.Normalize();
                    Vector3 _e2 = Vector3.Cross(_d, _e1);
                    float _fb = Mathf.Clamp(_u * Bins - 0.5f, 0f, Bins - 1.001f);
                    int _b0 = (int)_fb;
                    float _r = Mathf.Lerp(_radius[_b0], _radius[_b0 + 1], _fb - _b0) + _clearance;
                    float _angle = _q * _turns * Mathf.PI * 2f;
                    _path.Add(_c + (_e1 * Mathf.Cos(_angle) + _e2 * Mathf.Sin(_angle)) * _r);
                }
                return _path;
            }

            public Vector3 Outward(Vector3 _p)
            {
                Closest(_axis, _p, out Vector3 _c, out _);
                Vector3 _d = _p - _c;
                return _d.sqrMagnitude > 1e-8f ? _d.normalized : Vector3.up;
            }
        }
    }
}
