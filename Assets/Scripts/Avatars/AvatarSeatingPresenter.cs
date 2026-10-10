using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// Seated-ring presenter (LOCAL, presentation-only — NOT a NetworkBehaviour). Driven on/off by the
    /// <see cref="AvatarCameraArbiter"/> for the embodied Vote window.
    ///
    /// THE PER-CLIENT RING: while active, each LateUpdate it places EVERY avatar's BODY at its locally-computed
    /// ring pose (<see cref="AvatarManager.GetSeatPose"/>) — so the local player sits at the fixed FRONT spot
    /// and the others spread around by the shared relative order — and turns each avatar's HEAD (EyePivot) by
    /// its networked look (<see cref="PlayerAvatar.SeatedYaw"/> + <see cref="PlayerAvatar.SeatedPitch"/>,
    /// relative to seat facing) so remote viewers see where that player is looking. The head is INTERPOLATED
    /// toward its target (the look is published sparsely, on change) so remote gaze turns smoothly. Seated body
    /// positions are CLIENT-LOCAL, not networked.
    ///
    /// SUPPRESSION: the avatar body's pose normally rides an owner-authoritative <see cref="NetworkTransform"/>
    /// (one shared world pose per avatar). That is incompatible with a per-client rotated ring, so on enter we
    /// DISABLE each avatar's NetworkTransform (caching its current pose) and drive the transform locally; on
    /// exit we RESTORE the cached pose FIRST, then re-enable the NetworkTransform — otherwise every owner would
    /// re-broadcast its local-frame front-spot pose and all bodies would pile up on return to Board.
    ///
    /// Safe to write the transform directly: each suppressed avatar's CharacterController is disabled here too
    /// (alongside its NetworkTransform) so a direct write can never trigger a depenetration ejection — the
    /// "avatar flying up" class, commit 7bfcb42 — on the tight ring radius, on the owner OR a remote replica
    /// (the arbiter only disables the LOCAL owner's controller, so remote replicas would otherwise keep theirs).
    /// </summary>
    // Explicit order < AvatarHeadLook's 200: this presenter snaps the body root to the seat facing each
    // LateUpdate, and AvatarHeadLook aims the rigged head against that root frame — so it MUST run first.
    [DefaultExecutionOrder(100)]
    public class AvatarSeatingPresenter : MonoBehaviour
    {
        [Tooltip("How fast a remote avatar's head eases toward its networked look angle (higher = snappier). " +
                 "The look (yaw/pitch) is published sparsely (on change), so the head is interpolated here so " +
                 "remote gaze turns smoothly instead of stepping between updates.")]
        [SerializeField] private float _headLerpSpeed = 12f;

        private struct Suppressed
        {
            public NetworkTransform Nt;
            public bool WasEnabled;
            public CharacterController Cc;
            public bool CcWasEnabled;
            public Transform Eye;
            public Quaternion EyeLocalRotation;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        private bool _active;
        // Avatars whose NetworkTransform we suppressed this Vote, with the pose to restore on exit.
        private readonly Dictionary<PlayerAvatar, Suppressed> _suppressed = new();
        // Reused snapshot of the manager's live avatar list. We iterate a COPY because placing a seat below
        // (GetSeatPose -> SeatIndexForClient -> ResolvedAvatars) can re-enter the manager and rebuild — i.e.
        // Clear() — its SHARED avatar cache mid-iteration while a spawn is still in flight, which would throw
        // "Collection was modified" on the live list. Reused (cleared, not re-allocated) to stay alloc-free.
        private readonly List<PlayerAvatar> _avatarsSnapshot = new();

        /// <summary>Arbiter contract (mirror AvatarEmbodiedCamera.SetActive): on iff the mode is Embodied.</summary>
        public void SetActive(bool _isActive)
        {
            if (_isActive == _active)
            {
                return;
            }
            _active = _isActive;
            if (!_active)
            {
                RestoreAll();
            }
        }

        // Teardown safety (mirror AvatarEmbodiedCamera.OnDestroy): if this presenter is deactivated or
        // destroyed while still active (scene unload at match-end, host shutdown, rig torn down mid-Vote),
        // LateUpdate stops and RestoreAll would never run — leaving every avatar's NetworkTransform disabled
        // forever (bodies frozen, no position sync). Restore here so suppression can never outlive the presenter.
        private void OnDisable()
        {
            if (_active)
            {
                _active = false;
                RestoreAll();
            }
        }

        private void LateUpdate()
        {
            if (!_active)
            {
                return;
            }

            NetworkManager _networkManager = NetworkManager.Singleton;
            AvatarManager _manager = AvatarManager.For(_networkManager);
            if (_manager == null)
            {
                return;
            }

            // The ring is computed in the LOCAL client's frame (local avatar = front spot). Until the local
            // owned avatar is listed, SeatIndexForClient(localId) returns the not-found sentinel and every
            // seat would resolve to a wrong/colliding angle — so place nobody this frame.
            if (_networkManager == null || _manager.GetLocalAvatar() == null)
            {
                return;
            }

            // Snapshot the live list FIRST: GetSeatPose below re-enters the manager and may rebuild (Clear)
            // its shared avatar cache while a spawn is in flight — iterating the live list directly would
            // throw "Collection was modified". The copy goes into a reused buffer (alloc-free in steady state).
            _avatarsSnapshot.Clear();
            IReadOnlyList<PlayerAvatar> _live = _manager.GetAvatars();
            for (int _i = 0; _i < _live.Count; _i++)
            {
                _avatarsSnapshot.Add(_live[_i]);
            }

            for (int _i = 0; _i < _avatarsSnapshot.Count; _i++)
            {
                PlayerAvatar _avatar = _avatarsSnapshot[_i];
                if (_avatar == null)
                {
                    continue;
                }

                // An avatar whose identity has not replicated yet (still the FAKE_CLIENT_ID default) has no
                // resolved seat — placing it would collide it onto the sentinel seat with every other
                // unresolved avatar. Skip until its ownerClientId replicates (self-heals within a frame or two).
                if (_avatar.ownerClientId.Value == GameValues.FAKE_CLIENT_ID)
                {
                    continue;
                }

                Suppress(_avatar);

                // Body root: snap to the locally-computed seat pose, facing the table. Positions are local +
                // deterministic (not networked), so no position interpolation is needed.
                SeatPose _pose = _manager.GetSeatPose(_avatar.ownerClientId.Value);
                _avatar.transform.SetPositionAndRotation(_pose.Position, _pose.Rotation);
                // Seated size of the OTHER players: the pre-Vote scale times the manager's factor (restored on exit
                // with the pose). Never the local avatar: its EyePivot is the first-person camera's anchor, a smaller
                // local cat would lower the view (measured: vote buttons fell under the characters bar).
                Vector3 _scale = _suppressed[_avatar].Scale;
                _avatar.transform.localScale = _avatar.IsOwner ? _scale : _scale * _manager.SeatedAvatarScale;

                // Head (rigged): the networked look (yaw/pitch RELATIVE to seat facing) drives the EyePivot's
                // LOCAL rotation, so the head turns on the body that faces the table. Interpolated toward the
                // target because the look is published sparsely (on change) — smooth turn, no stepping.
                Transform _eye = _avatar.EyePivot;
                if (_eye != null)
                {
                    // Owner-write SeatedYaw/Pitch is UNTRUSTED input — a NaN/Inf publish is sticky through the
                    // slerp and would permanently corrupt the head (mirror AvatarHeadLook.Sanitize).
                    float _pitch = Sanitize(_avatar.SeatedPitch.Value);
                    float _yaw = Sanitize(_avatar.SeatedYaw.Value);
                    Quaternion _targetLook = Quaternion.Euler(_pitch, _yaw, 0f);
                    float _t = 1f - Mathf.Exp(-_headLerpSpeed * Time.deltaTime); // framerate-independent ease
                    _eye.localRotation = Quaternion.Slerp(_eye.localRotation, _targetLook, _t);
                }
            }
        }

        // Owner-write replicated look is untrusted: a NaN/Inf publish is sticky through the slerp (mirror
        // AvatarHeadLook.Sanitize). Clamp non-finite to 0 so a bad publish cannot permanently corrupt the head.
        private static float Sanitize(float _value) => float.IsFinite(_value) ? _value : 0f;

        // Disable the avatar's NetworkTransform once (caching its live pose for a clean restore). Idempotent.
        private void Suppress(PlayerAvatar _avatar)
        {
            if (_suppressed.ContainsKey(_avatar))
            {
                return;
            }

            NetworkTransform _nt = _avatar.GetComponent<NetworkTransform>();
            CharacterController _cc = _avatar.GetComponent<CharacterController>();
            Transform _eye = _avatar.EyePivot;
            Transform _t = _avatar.transform;
            _suppressed[_avatar] = new Suppressed
            {
                Nt = _nt,
                WasEnabled = _nt != null && _nt.enabled,
                Cc = _cc,
                CcWasEnabled = _cc != null && _cc.enabled,
                Eye = _eye,
                EyeLocalRotation = _eye != null ? _eye.localRotation : Quaternion.identity,
                Position = _t.position,
                Rotation = _t.rotation,
                Scale = _t.localScale,
            };
            if (_nt != null)
            {
                _nt.enabled = false;
            }
            // Disable the controller so a direct transform write can't depenetrate-eject on the ring.
            if (_cc != null)
            {
                _cc.enabled = false;
            }
        }

        // Restore every suppressed avatar's pre-Vote pose, THEN re-enable its NetworkTransform so the
        // owner-authoritative sync resumes from the real pose (never the local-frame seated one).
        private void RestoreAll()
        {
            foreach (KeyValuePair<PlayerAvatar, Suppressed> _entry in _suppressed)
            {
                PlayerAvatar _avatar = _entry.Key;
                Suppressed _state = _entry.Value;
                if (_avatar != null)
                {
                    _avatar.transform.SetPositionAndRotation(_state.Position, _state.Rotation);
                    _avatar.transform.localScale = _state.Scale;
                }
                // Put the head back where it was so it doesn't stay turned after the Vote.
                if (_state.Eye != null)
                {
                    _state.Eye.localRotation = _state.EyeLocalRotation;
                }
                // Restore the controller at the (valid, non-overlapping) pre-Vote pose, then re-enable the
                // NetworkTransform so owner-authoritative sync resumes from the real pose, not the seated one.
                if (_state.Cc != null)
                {
                    _state.Cc.enabled = _state.CcWasEnabled;
                }
                if (_state.Nt != null)
                {
                    _state.Nt.enabled = _state.WasEnabled;
                }
            }
            _suppressed.Clear();
        }
    }
}
