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
    /// The label is a runtime-built world-space <see cref="TextMeshPro"/> (no prefab asset, TMP default font) —
    /// placeholder visuals, every size/colour serialized so they are tuned in the Inspector, not enshrined here.
    /// Lives on the <see cref="PlayerAvatar"/> prefab root; the text GO is parented to it (cleaned up with the
    /// avatar) but its world pose is driven each <see cref="LateUpdate"/> so head yaw/pitch never skews it.
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public class AvatarNameplate : MonoBehaviour
    {
        [Tooltip("Shared camera-mode channel (same asset AvatarCameraArbiter writes). Gates first-person visibility.")]
        [SerializeField] private CameraModeChannel _cameraModeChannel;

        [Tooltip("Optional anchor override. Empty = the avatar's EyePivot (head height).")]
        [SerializeField] private Transform _anchorOverride;

        [Header("Placeholder visuals — provisional, tune in the Inspector")]
        [Tooltip("Metres above the head anchor.")]
        [SerializeField] private float _worldHeightOffset = 0.5f;

        [SerializeField] private float _fontSize = 8f;

        [Tooltip("Uniform world scale of the text object (shrinks the world-space TMP to table scale).")]
        [SerializeField] private float _worldScale = 0.1f;

        [SerializeField] private Color _color = Color.white;

        [Tooltip("Optional font override. Empty = TMP_Settings.defaultFontAsset.")]
        [SerializeField] private TMP_FontAsset _fontOverride;

        private PlayerAvatar _avatar;
        private LobbyPlayerInfoHolder _holder;
        private Transform _anchor;
        private TextMeshPro _tmp;
        private Camera _cam;

        private bool _initialized;
        private bool _localSuppressed; // this is my OWN avatar → never show a plate

        private void Awake() => _avatar = GetComponent<PlayerAvatar>();

        // Lazy init: the avatar's NetworkObject must be spawned (so ownerClientId + IsOwner are meaningful) AND
        // the lobby holder must be resolvable (so the pseudo exists). Both hold shortly after the avatar spawns
        // in a live match; we simply retry until then.
        private void Update()
        {
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

            // Never show the local player's own nameplate — build nothing (also saves the per-frame billboard).
            if (_avatar.IsOwner)
            {
                _localSuppressed = true;
                return;
            }

            _anchor = _anchorOverride != null ? _anchorOverride
                : (_avatar.EyePivot != null ? _avatar.EyePivot : transform);

            BuildText();

            if (_holder != null && _holder.playerInfos != null)
            {
                _holder.playerInfos.OnListChanged += OnPlayerInfosChanged;
            }
            RefreshText();
        }

        // Build the world-space label. RectTransform first so the TMP RequireComponent is satisfied at runtime.
        private void BuildText()
        {
            var _go = new GameObject("Nameplate", typeof(RectTransform), typeof(TextMeshPro));
            _go.transform.SetParent(transform, false);
            _go.transform.localScale = Vector3.one * _worldScale;

            _tmp = _go.GetComponent<TextMeshPro>();
            _tmp.font = _fontOverride != null ? _fontOverride : TMP_Settings.defaultFontAsset;
            _tmp.fontSize = _fontSize;
            _tmp.color = _color;
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.overflowMode = TextOverflowModes.Overflow;
            _tmp.raycastTarget = false;

            var _rt = _tmp.rectTransform;
            _rt.sizeDelta = new Vector2(4f, 1f);

            _go.SetActive(false); // hidden until the first-person gate says otherwise
        }

        private void OnPlayerInfosChanged(NetworkListEvent<PlayerInfo> _evt) => RefreshText();

        private void RefreshText()
        {
            if (_tmp == null)
            {
                return;
            }
            _tmp.text = NameplatePolicy.ResolveLabel(ResolvePseudo());
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

        private void LateUpdate()
        {
            if (!_initialized || _localSuppressed || _tmp == null)
            {
                return;
            }

            bool _show = NameplatePolicy.ShouldShow(false, IsFirstPerson());
            if (_tmp.gameObject.activeSelf != _show)
            {
                _tmp.gameObject.SetActive(_show);
            }
            if (!_show)
            {
                return;
            }

            Camera _camera = ResolveCamera();
            if (_camera == null)
            {
                return;
            }

            Transform _t = _tmp.transform;
            _t.position = _anchor.position + Vector3.up * _worldHeightOffset;
            // Billboard using the project's proven face-camera math (mirrors CharactersBarObject.GetFaceCameraRotation):
            // the world-space text front is visible from the opposite side, so forward points AWAY from the
            // camera; the camera's up keeps the text upright.
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
