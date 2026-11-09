using System;
using System.Collections.Generic;
using Avatars;
using CorruptionDuPortail.Domain;
using FX;
using GameLogic;
using GameLogic.GameStates;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;

namespace Presentation
{
    /// <summary>
    /// T27 (mockups bouton-skip v1 H3 T1 B2 C1 R1 E1 K2, v2 S2): the way to skip the vote by day, a low round table in the
    /// middle of the circle with a carved wooden button. It rises from the floor when the vote starts and sinks at the
    /// end of the recap (the chained cat comes to the middle). Pointing at the button says what a click does above it; a
    /// click votes for nobody, and the button stays down and outlined for this player alone. During the recap the number
    /// of skips stands above the cards, on the button's axis. Local presentation; the vote itself goes through
    /// <see cref="DayCatVoting.SkipFromButton"/> (the server decides). Turned from code like the vote candle.
    /// </summary>
    [DefaultExecutionOrder(320)] // after the seats and the camera, for the texts to face this frame's eye
    public class DaySkipTable : MonoBehaviour
    {
        private const string HoverText = "Cliquer pour skip le vote";
        private const int PhysicalLayer = 7; // 3DPhysical: the only layer the reticle and the cursor's raycaster see
        private const int Segments = 48;

        // Lane A: scene-wired.
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private DayCatVoting _dayVoting;
        [SerializeField] private CameraModeChannel _cameraMode;
        [SerializeField] private DayTableTransition _day;
        [SerializeField] private AvatarEmbodiedCamera _embodiedCamera;
        [SerializeField] private CatSpotlights _spotlights;
        [Tooltip("The day's floor: the table stands on it.")]
        [SerializeField] private Transform _floor;
        [Tooltip("Disabled spot light copied once (the cats' template), hung straight above the table.")]
        [SerializeField] private Light _lightTemplate;
        [SerializeField] private TMP_FontAsset _font;
        [Tooltip("Foot, column and the button's seat.")]
        [SerializeField] private Material _darkMaterial;
        [Tooltip("The table's top: the wood of the artist's table.")]
        [SerializeField] private Material _topMaterial;
        [SerializeField] private Material _buttonMaterial;
        [Tooltip("The drawing carved on the button.")]
        [SerializeField] private Material _carvingMaterial;

        [SerializeField] private float _riseSeconds = 0.7f;
        [SerializeField] private float _sinkSeconds = 0.5f;
        [Tooltip("How far the button goes down once pressed (metres).")]
        [SerializeField] private float _pressDepth = 0.42f;
        [Tooltip("Distance from the table's top to its light (metres).")]
        [SerializeField] private float _lightHeight = 30f;
        [SerializeField] private float _lightAngle = 22f;
        [Tooltip("The table's light against a cat's.")]
        [SerializeField] private float _lightShare = 0.7f;
        [Tooltip("The texts' size: 10 = 1 metre between lines, 34 pixels on a 900-pixel screen at the circle's middle.")]
        [SerializeField] private float _fontSize = 11f;
        [Tooltip("The pointed text's centre above the button (metres).")]
        [SerializeField] private float _hoverTextAbove = 2.1f;

        private IGameStateQuery Query => _gameManager;

        private Transform _root;
        private Transform _text;
        private GameObject _button;
        private MeshFilter _tableMesh;
        private Collider _buttonCollider;
        private TextMeshPro _hoverLabel;
        private TextMeshPro _recapLabel;
        private Light _light;
        private float _tableHeight;
        private float _floorY;
        private float _level;
        private float _press;
        private bool _hovered;
        private bool _recapOver;
        private VoteRecapState _watchedRecap;
        private OutlineState _outline = OutlineState.None;

        /// <summary>The object a click must land on (autoplay's real input aims at it); null before the first vote.</summary>
        public GameObject Button => _button;

        /// <summary>The table has finished rising: the button answers.</summary>
        public bool IsUp => _level >= 1f;

        /// <summary>The recap's number of skips is on show.</summary>
        public bool RecapShown => _recapLabel != null && _recapLabel.gameObject.activeSelf;

        private void Awake()
        {
            Assert.IsNotNull(_gameManager, "DaySkipTable._gameManager is not wired — wire the GameManager in GameScene.");
            Assert.IsNotNull(_dayVoting, "DaySkipTable._dayVoting is not wired — wire the DayCatVoting in GameScene.");
            Assert.IsNotNull(_cameraMode, "DaySkipTable._cameraMode is not wired — wire the CameraModeChannel asset.");
            Assert.IsNotNull(_day, "DaySkipTable._day is not wired — wire the DayTableTransition in GameScene.");
            Assert.IsNotNull(_embodiedCamera, "DaySkipTable._embodiedCamera is not wired — wire the AvatarEmbodiedCamera.");
            Assert.IsNotNull(_spotlights, "DaySkipTable._spotlights is not wired — wire the CatSpotlights in GameScene.");
            Assert.IsNotNull(_floor, "DaySkipTable._floor is not wired — wire the day's floor in GameScene.");
            Assert.IsNotNull(_lightTemplate, "DaySkipTable._lightTemplate is not wired — wire the cats' light template.");
            Assert.IsNotNull(_font, "DaySkipTable._font is not wired — wire EB Garamond SDF.");
        }

        private void OnDestroy()
        {
            WatchRecap(null);
            if (_root != null)
            {
                Destroy(_root.gameObject);
            }
            if (_text != null)
            {
                Destroy(_text.gameObject);
            }
        }

        private void Update()
        {
            GameState _state = Query.GetGameState(Query.currentGameStateIndex.Value);
            if (_state is VoteRecapState _recap)
            {
                WatchRecap(_recap);
            }
            else
            {
                _recapOver = false;
            }

            bool _wanted = _cameraMode.Current == CameraMode.Embodied && _day.DaySettled && !_recapOver
                           && ((_state is VoteState _vote && _vote.voteOnCats) || _state is VoteRecapState);
            if (_wanted && _level <= 0f && !Place())
            {
                return;
            }
            if (_root == null)
            {
                return;
            }

            float _before = _level;
            float _seconds = _wanted ? _riseSeconds : _sinkSeconds;
            _level = Mathf.MoveTowards(_level, _wanted ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, _seconds));
            if (_before != _level && (_level >= 1f || _level <= 0f))
            {
                Diagnostics.Logs.GameLog.Info("ui", "day.skiptable", "up", _level >= 1f, "height", _tableHeight);
            }
            bool _shown = _level > 0f;
            if (_root.gameObject.activeSelf != _shown)
            {
                _root.gameObject.SetActive(_shown);
            }
            if (!_shown)
            {
                _hovered = false;
                SetRecapLabel(null);
                _hoverLabel.gameObject.SetActive(false);
                return;
            }

            // All of it under the floor at level 0: the floor hides it on the way.
            float _eased = Mathf.SmoothStep(0f, 1f, _level);
            Vector3 _position = _root.position;
            _position.y = _floorY - (_tableHeight + 1f) * (1f - _eased);
            _root.position = _position;

            bool _voting = _state is VoteState;
            _buttonCollider.enabled = _voting && IsUp;
            if (!_buttonCollider.enabled)
            {
                _hovered = false; // a disabled collider gets no exit event
            }
            bool _pressed = _dayVoting.LocalSkipped;
            _press = Mathf.MoveTowards(_press, _pressed ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            _button.transform.localPosition = new Vector3(0f, _tableHeight - _pressDepth * _press, 0f);
            SetOutline(_pressed ? OutlineState.Selected : _hovered && _dayVoting.SkipReachable ? OutlineState.Hover : OutlineState.None);

            _hoverLabel.gameObject.SetActive(_hovered && _dayVoting.SkipReachable);
            _light.intensity = _lightTemplate.intensity * _lightShare * _eased * _spotlights.Dim * _spotlights.Darken;
        }

        private void LateUpdate()
        {
            Camera _camera = Camera.main;
            if (_camera == null || _text == null)
            {
                return;
            }
            if (_hoverLabel.gameObject.activeSelf)
            {
                _hoverLabel.transform.rotation = Quaternion.LookRotation(_hoverLabel.transform.position - _camera.transform.position);
            }
            if (_recapLabel.gameObject.activeSelf)
            {
                _recapLabel.transform.rotation = Quaternion.LookRotation(_recapLabel.transform.position - _camera.transform.position);
            }
        }

        // ---- where it stands, for this player's eye

        private bool Place()
        {
            AvatarManager _manager = AvatarManager.For(NetworkManager.Singleton);
            Vector3? _centre = _manager != null ? _manager.RingCenter : null;
            float? _eyeAboveSeat = _embodiedCamera.EyeHeightAboveSeat;
            if (_centre == null || _eyeAboveSeat == null)
            {
                Diagnostics.Logs.GameLog.Warn("ui", "day.skiptable.skipped", "reason", _centre == null ? "no-centre" : "no-eye");
                return false;
            }
            if (_root == null)
            {
                Build();
            }
            Vector3 _seat = _manager.GetLocalSeatPose().Position;
            _floorY = _floor.position.y;
            float _eyeAboveFloor = _seat.y + _eyeAboveSeat.Value - _floorY;
            Vector3 _toMiddle = _centre.Value - _seat;
            _toMiddle.y = 0f;
            float _distance = _toMiddle.magnitude;
            float _height = DaySkipTableLayout.TableHeight(_eyeAboveFloor, _distance);
            if (!Mathf.Approximately(_height, _tableHeight))
            {
                _tableHeight = _height;
                if (_tableMesh.sharedMesh != null)
                {
                    Destroy(_tableMesh.sharedMesh);
                }
                _tableMesh.sharedMesh = TableMesh(_height);
            }
            // The carving reads upright from this seat.
            Quaternion _facing = _distance > 1e-3f ? Quaternion.LookRotation(_toMiddle / _distance, Vector3.up) : Quaternion.identity;
            _button.transform.localRotation = _facing;
            _root.position = new Vector3(_centre.Value.x, _floorY - _height - 1f, _centre.Value.z);
            _hoverLabel.transform.position = new Vector3(_centre.Value.x, _floorY + _height + _hoverTextAbove, _centre.Value.z);
            _recapLabel.transform.position = new Vector3(_centre.Value.x,
                _floorY + DaySkipTableLayout.RecapTextHeight(_eyeAboveFloor, _distance), _centre.Value.z);
            _light.transform.localPosition = Vector3.up * (_height + _lightHeight);
            return true;
        }

        // ---- the recap's number

        private void WatchRecap(VoteRecapState _recap)
        {
            if (_watchedRecap == _recap)
            {
                return;
            }
            if (_watchedRecap != null)
            {
                _watchedRecap.onShowVoteRecap -= OnRecapShown;
                _watchedRecap.onHideVoteRecap -= OnRecapHidden;
            }
            _watchedRecap = _recap;
            if (_watchedRecap != null)
            {
                _watchedRecap.onShowVoteRecap += OnRecapShown;
                _watchedRecap.onHideVoteRecap += OnRecapHidden;
            }
        }

        private void OnRecapShown()
        {
            VoteState _vote = _dayVoting.DayVote();
            int _skips = _vote != null && _vote.votesForPlayer.TryGetValue(VoteState.SKIP_VOTE_ID, out List<ulong> _voters) ? _voters.Count : 0;
            Diagnostics.Logs.GameLog.Info("vote", "vote.skip.recap", "skips", _skips);
            SetRecapLabel($"Skips : {_skips}");
        }

        private void OnRecapHidden()
        {
            _recapOver = true;
            SetRecapLabel(null);
        }

        private void SetRecapLabel(string _content)
        {
            if (_recapLabel == null)
            {
                return;
            }
            _recapLabel.gameObject.SetActive(_content != null);
            if (_content != null)
            {
                _recapLabel.text = _content;
            }
        }

        // ---- the button

        private void OnButtonEntered()
        {
            _hovered = true;
        }

        private void OnButtonExited()
        {
            _hovered = false;
        }

        private void OnButtonClicked()
        {
            _dayVoting.SkipFromButton();
        }

        private void SetOutline(OutlineState _state)
        {
            if (_outline != _state)
            {
                _outline = _state;
                _button.SetOutline(_state);
            }
        }

        // ---- the objects

        private void Build()
        {
            var _rootObject = new GameObject("DaySkipTable");
            _root = _rootObject.transform;
            _tableMesh = _rootObject.AddComponent<MeshFilter>();
            _rootObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { _darkMaterial, _topMaterial };

            _button = new GameObject("Button");
            _button.layer = PhysicalLayer;
            _button.transform.SetParent(_root, false);
            _button.AddComponent<MeshFilter>().sharedMesh = ButtonMesh();
            _button.AddComponent<MeshRenderer>().sharedMaterials = new[] { _darkMaterial, _buttonMaterial, _carvingMaterial };
            var _collider = _button.AddComponent<CapsuleCollider>();
            _collider.isTrigger = true;
            _collider.direction = 1;
            _collider.radius = 2.2f;
            _collider.height = 4.6f;
            _collider.center = Vector3.up * 0.4f;
            _collider.enabled = false;
            _buttonCollider = _collider;
            var _pointer = _button.AddComponent<DaySkipButtonTarget>();
            _pointer.Entered += OnButtonEntered;
            _pointer.Exited += OnButtonExited;
            _pointer.Clicked += OnButtonClicked;

            _light = Instantiate(_lightTemplate, _root);
            _light.name = "Light";
            _light.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _light.spotAngle = _lightAngle;
            _light.innerSpotAngle = _lightAngle * 0.6f;
            _light.shadows = LightShadows.None;
            _light.enabled = true;

            // Apart from the table: they do not rise with it.
            _text = new GameObject("DaySkipTexts").transform;
            _hoverLabel = NewLabel("Hover");
            _hoverLabel.text = HoverText;
            _recapLabel = NewLabel("Recap");
        }

        private TextMeshPro NewLabel(string _name)
        {
            var _object = new GameObject(_name);
            _object.transform.SetParent(_text, false);
            var _label = _object.AddComponent<TextMeshPro>();
            _label.font = _font;
            _label.fontSize = _fontSize;
            _label.color = new Color(1f, 0.953f, 0.855f, 1f); // the vote hint's #fff3da
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.rectTransform.sizeDelta = new Vector2(60f, 8f);
            _object.SetActive(false);
            return _label;
        }

        // Foot, column, under the top (dark, submesh 0); the top (wood, submesh 1). The voted shape: 7.2 m across.
        internal static Mesh TableMesh(float _height)
        {
            var _builder = new TurnedMesh(2);
            _builder.AddFrustum(0, 2.4f, 0f, 1.2f, 0.35f);
            _builder.AddFrustum(0, 0.42f, 0.3f, 0.42f, _height - 0.5f);
            _builder.AddFrustum(0, 1.6f, Mathf.Max(0.3f, _height - 0.9f), 3.1f, _height - 0.42f);
            _builder.AddFrustum(1, 3.6f, _height - 0.42f, 3.6f, _height);
            return _builder.ToMesh("DaySkipTable");
        }

        // The seat (submesh 0), the wooden puck (1), the carved drawing laid on it (2, transparent).
        internal static Mesh ButtonMesh()
        {
            var _builder = new TurnedMesh(3);
            _builder.AddFrustum(0, 2.05f, 0f, 1.95f, 0.22f);
            _builder.AddFrustum(1, 1.65f, 0.2f, 1.55f, 0.82f);
            _builder.AddQuad(2, 1.45f, 0.83f);
            return _builder.ToMesh("DaySkipButton");
        }

        // Round shapes around the Y axis, a flat colour or a planar wood on the caps.
        private sealed class TurnedMesh
        {
            private readonly List<Vector3> _vertices = new();
            private readonly List<Vector3> _normals = new();
            private readonly List<Vector2> _uvs = new();
            private readonly List<int>[] _triangles;

            public TurnedMesh(int _submeshes)
            {
                _triangles = new List<int>[_submeshes];
                for (int _i = 0; _i < _submeshes; _i++)
                {
                    _triangles[_i] = new List<int>();
                }
            }

            public void AddFrustum(int _submesh, float _bottomRadius, float _bottomY, float _topRadius, float _topY)
            {
                Vector2 _slope = new Vector2(_topY - _bottomY, _bottomRadius - _topRadius).normalized;
                int _side = _vertices.Count;
                for (int _i = 0; _i <= Segments; _i++)
                {
                    float _turn = (float)_i / Segments;
                    Vector3 _out = Around(_turn);
                    AddVertex(_out * _bottomRadius + Vector3.up * _bottomY, _out * _slope.x + Vector3.up * _slope.y, new Vector2(_turn * 3f, 0f));
                    AddVertex(_out * _topRadius + Vector3.up * _topY, _out * _slope.x + Vector3.up * _slope.y, new Vector2(_turn * 3f, (_topY - _bottomY) / 7f));
                }
                for (int _i = 0; _i < Segments; _i++)
                {
                    int _a = _side + _i * 2;
                    Triangle(_submesh, _a, _a + 1, _a + 3);
                    Triangle(_submesh, _a, _a + 3, _a + 2);
                }
                AddCap(_submesh, _topRadius, _topY, true);
                AddCap(_submesh, _bottomRadius, _bottomY, false);
            }

            public void AddQuad(int _submesh, float _half, float _y)
            {
                int _start = _vertices.Count;
                AddVertex(new Vector3(-_half, _y, -_half), Vector3.up, new Vector2(0f, 0f));
                AddVertex(new Vector3(-_half, _y, _half), Vector3.up, new Vector2(0f, 1f));
                AddVertex(new Vector3(_half, _y, _half), Vector3.up, new Vector2(1f, 1f));
                AddVertex(new Vector3(_half, _y, -_half), Vector3.up, new Vector2(1f, 0f));
                Triangle(_submesh, _start, _start + 1, _start + 2);
                Triangle(_submesh, _start, _start + 2, _start + 3);
            }

            public Mesh ToMesh(string _name)
            {
                var _mesh = new Mesh { name = _name, subMeshCount = _triangles.Length };
                _mesh.SetVertices(_vertices);
                _mesh.SetNormals(_normals);
                _mesh.SetUVs(0, _uvs);
                for (int _i = 0; _i < _triangles.Length; _i++)
                {
                    _mesh.SetTriangles(_triangles[_i], _i);
                }
                _mesh.RecalculateBounds();
                return _mesh;
            }

            private void AddCap(int _submesh, float _radius, float _y, bool _up)
            {
                int _centre = _vertices.Count;
                Vector3 _normal = _up ? Vector3.up : Vector3.down;
                AddVertex(Vector3.up * _y, _normal, new Vector2(0.5f, 0.5f));
                for (int _i = 0; _i <= Segments; _i++)
                {
                    Vector3 _out = Around((float)_i / Segments);
                    AddVertex(_out * _radius + Vector3.up * _y, _normal, new Vector2(0.5f + _out.x * 0.5f, 0.5f + _out.z * 0.5f));
                }
                for (int _i = 0; _i < Segments; _i++)
                {
                    int _a = _centre + 1 + _i;
                    if (_up) Triangle(_submesh, _centre, _a, _a + 1);
                    else Triangle(_submesh, _centre, _a + 1, _a);
                }
            }

            private static Vector3 Around(float _turn)
            {
                float _angle = _turn * Mathf.PI * 2f;
                return new Vector3(Mathf.Sin(_angle), 0f, Mathf.Cos(_angle));
            }

            private void AddVertex(Vector3 _position, Vector3 _normal, Vector2 _uv)
            {
                _vertices.Add(_position);
                _normals.Add(_normal);
                _uvs.Add(_uv);
            }

            private void Triangle(int _submesh, int _a, int _b, int _c)
            {
                _triangles[_submesh].Add(_a);
                _triangles[_submesh].Add(_b);
                _triangles[_submesh].Add(_c);
            }
        }
    }

    /// <summary>The skip button as something to point at: the seated reticle and a free cursor deliver the events here.</summary>
    public class DaySkipButtonTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public event Action Entered;
        public event Action Exited;
        public event Action Clicked;

        public void OnPointerEnter(PointerEventData _eventData) => Entered?.Invoke();

        public void OnPointerExit(PointerEventData _eventData) => Exited?.Invoke();

        public void OnPointerClick(PointerEventData _eventData) => Clicked?.Invoke();
    }
}
