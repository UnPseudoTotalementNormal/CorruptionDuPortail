using GameLogic;
using Network;
using Network.Player;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// Floats the owning player's pseudo above their 3D avatar's head. PURE presentation, client-local: it
    /// reads already-replicated values (<see cref="PlayerAvatar.ownerClientId"/> + the lobby name table) and
    /// adds NO RPC / NetworkVariable / server state (NFR3 — server authority untouched).
    ///
    /// Design rules (Poyo, 2026-07-13), encoded in <see cref="NameplatePolicy"/>:
    ///  • shown for OTHER players only — never the local player's OWN avatar (you don't see your own head in
    ///    first-person);
    ///  • shown ONLY while the local camera is first-person (FreeRoam walk-around OR the seated Embodied vote
    ///    with the first-person node live), gated on the shared <see cref="CameraModeChannel"/> — the same
    ///    predicate the <see cref="AvatarCameraArbiter"/> uses.
    ///
    /// The label is a REAL <see cref="TextMeshPro"/> child placed in the PlayerAvatar prefab (wired into
    /// <see cref="_label"/>) — you keep its TRANSFORM (position/scale) in the editor. Its FONT SIZE is driven by
    /// <see cref="_fontSize"/>: this component runs in edit mode too (<c>[ExecuteAlways]</c>) so tweaking that
    /// field updates the label's font size live, WYSIWYG. At runtime it also fills the text, gates visibility,
    /// and billboards the label toward the camera. Parent the label under the avatar ROOT (not a rig bone) so
    /// head yaw/pitch never drags it; the runtime billboard overrides only its rotation, never its transform.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(PlayerAvatar))]
    public class AvatarNameplate : MonoBehaviour
    {
        [Tooltip("Shared camera-mode channel (same asset AvatarCameraArbiter writes). Gates first-person visibility.")]
        [SerializeField] private CameraModeChannel _cameraModeChannel;

        [Tooltip("The world-space label child (a TextMeshPro placed in the prefab). Position it above the head " +
                 "here in the editor — this component fills its text, sizes it, shows/hides it, and faces it to " +
                 "the camera.")]
        [SerializeField] private TextMeshPro _label;

        [Tooltip("Font size applied to the label. Driven by the script (live in edit mode); you keep the child's transform.")]
        [SerializeField] private float _fontSize = 8f;

        private PlayerAvatar _avatar;
        private LobbyPlayerInfoHolder _holder;
        private Camera _cam;

        private bool _initialized;
        private bool _localSuppressed; // this is my OWN avatar → never show a plate

        private void Awake()
        {
            _avatar = GetComponent<PlayerAvatar>();
            if (Application.isPlaying)
            {
                SetLabelActive(false); // hidden until the first-person gate says otherwise
            }
        }

        // Keep the label's font size in sync with _fontSize the moment it's edited in the Inspector (edit mode).
        private void OnValidate() => ApplyFontSize();

        // Font-size drive. Runs in edit mode too (ExecuteAlways) so the size is WYSIWYG; only writes on a real
        // change so it never spams the editor dirty flag.
        private void ApplyFontSize()
        {
            if (_label == null)
            {
                return;
            }
            if (!Mathf.Approximately(_label.fontSize, _fontSize))
            {
                _label.fontSize = _fontSize;
            }
        }

        // Lazy init: the avatar's NetworkObject must be spawned (so ownerClientId + IsOwner are meaningful) AND
        // the lobby holder must be resolvable (so the pseudo exists). Both hold shortly after the avatar spawns
        // in a live match; we simply retry until then.
        private void Update()
        {
            ApplyFontSize(); // edit mode + play: keep the label sized

            // Everything below is runtime-only — ExecuteAlways would otherwise run the network/gate logic in the
            // editor (NetworkManager is null there), and hide the label you are trying to tune.
            if (!Application.isPlaying)
            {
                return;
            }

            if (_initialized || _avatar == null || !_avatar.IsSpawned)
            {
                return;
            }

            if (_holder == null)
            {
                // Resolve via the composition root only (mirrors Character.cs:50 — the DI seam, NOT the static
                // `.instance` façade). Null-tolerant: if the lobby holder is not registered yet, retry next frame.
                NetworkManager _nm = _avatar.NetworkManager;
                _holder = _nm != null ? CompositionRoot.For(_nm).LobbyPlayerInfoHolder : null;
                if (_holder == null)
                {
                    return; // holder not up yet — try again next frame
                }
            }

            Initialize();
        }

        private void Initialize()
        {
            _initialized = true;

            // Never show the local player's own nameplate.
            if (_avatar.IsOwner)
            {
                _localSuppressed = true;
                SetLabelActive(false);
                return;
            }

            if (_holder != null && _holder.playerInfos != null)
            {
                _holder.playerInfos.OnListChanged += OnPlayerInfosChanged;
            }
            RefreshText();
        }

        private void OnPlayerInfosChanged(NetworkListEvent<PlayerInfo> _evt) => RefreshText();

        private void RefreshText()
        {
            if (_label == null)
            {
                return;
            }
            _label.text = NameplatePolicy.ResolveLabel(ResolvePseudo());
        }

        private string ResolvePseudo()
        {
            if (_holder == null)
            {
                return string.Empty;
            }
            return _holder.GetPlayerInfo(_avatar.ownerClientId.Value).playerName.ToString();
        }

        private bool IsFirstPerson() =>
            _cameraModeChannel != null &&
            (_cameraModeChannel.Current == CameraMode.FreeRoam || _cameraModeChannel.SeatedFirstPersonLive);

        private Camera ResolveCamera()
        {
            if (_cam == null || !_cam.isActiveAndEnabled)
            {
                _cam = Camera.main;
            }
            return _cam;
        }

        private void SetLabelActive(bool _active)
        {
            if (_label != null && _label.gameObject.activeSelf != _active)
            {
                _label.gameObject.SetActive(_active);
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !_initialized || _localSuppressed || _label == null)
            {
                return;
            }

            bool _show = NameplatePolicy.ShouldShow(false, IsFirstPerson());
            SetLabelActive(_show);
            if (!_show)
            {
                return;
            }

            Camera _camera = ResolveCamera();
            if (_camera == null)
            {
                return;
            }

            // Billboard only — height/size come from the label child's transform in the prefab. Uses the
            // project's proven face-camera math (mirrors CharactersBarObject.GetFaceCameraRotation): the text
            // front is visible from the opposite side, so forward points AWAY from the camera; camera up keeps
            // the text upright.
            Transform _t = _label.transform;
            Vector3 _awayFromCamera = _t.position - _camera.transform.position;
            _t.rotation = Quaternion.LookRotation(_awayFromCamera, _camera.transform.up);
        }

        private void OnDestroy()
        {
            if (_holder != null && _holder.playerInfos != null)
            {
                _holder.playerInfos.OnListChanged -= OnPlayerInfosChanged;
            }
        }
    }
}
