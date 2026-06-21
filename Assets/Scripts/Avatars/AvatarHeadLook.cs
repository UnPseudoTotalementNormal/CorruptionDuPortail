using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// Procedural rigged head-look (LOCAL presentation; runs on EVERY client — owner and remotes). Each
    /// LateUpdate, AFTER the Animator/NetworkAnimator has written the rig, it aims the rigged HEAD BONE by the
    /// avatar's networked head-look (<see cref="PlayerAvatar.SeatedYaw"/> / <see cref="PlayerAvatar.SeatedPitch"/>,
    /// "head direction RELATIVE to body facing"), so the rendered cat's head visibly pitches and turns for
    /// everyone — the "where are they looking" tell the social-deduction game lives on.
    ///
    /// Why ease the ANGLES, not the bone: the Animator overwrites the head bone every frame, so we cannot
    /// accumulate a slerp on the bone itself. We keep our own displayed yaw/pitch, ease THOSE toward the
    /// networked look (smoothing sparse remote updates), and re-apply the FULL eased aim to the freshly
    /// animated bone each frame in the body (root) frame: yaw about body up, pitch about body right.
    ///
    /// Execution order is forced AFTER <see cref="AvatarSeatingPresenter"/> (which snaps the body root to the
    /// seat facing during the embodied Vote) so the root up/right axes we aim against are already correct.
    ///
    /// PLACEMENT: this component lives on the MODEL prefab (Cat_Avatar) alongside the rig, so the
    /// <see cref="_headBone"/> reference is local + stable. The look channel + body frame come from the
    /// <see cref="PlayerAvatar"/> resolved in the parent — so a model nested under a PlayerAvatar is driven,
    /// and a standalone model (no PlayerAvatar parent) simply no-ops.
    ///
    /// Presentation-only (NFR3): reads replicated state, writes only a bone transform — no game state, no RPC.
    /// v1 OVERRIDES the clip's head keys; additive blend over the animation is a later tuning pass.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class AvatarHeadLook : MonoBehaviour
    {
        [Tooltip("The rigged HEAD bone of this avatar model (e.g. 'BW Rig/.../Head'). Aimed each LateUpdate. " +
                 "Lives in the same model prefab as this component, so the reference is local + stable.")]
        [SerializeField] private Transform _headBone;
        [Tooltip("How fast the displayed head aim eases toward the networked look (higher = snappier). " +
                 "Smooths the sparse remote updates and the owner's own per-frame look. Poyo-tuned.")]
        [SerializeField] private float _aimEaseSpeed = 12f;

        // Resolved from the parent at Awake: the PlayerAvatar carries the SeatedYaw/Pitch look channel, and its
        // transform is the BODY frame the head aims relative to. Null when the model has no PlayerAvatar parent
        // (e.g. the standalone Cat_Avatar prefab) → the component no-ops.
        private PlayerAvatar _avatar;
        private Transform _root;
        private float _displayedYaw;
        private float _displayedPitch;
        private bool _seeded;
        private bool _warned;

        private void Awake()
        {
            _avatar = GetComponentInParent<PlayerAvatar>();
            _root = _avatar != null ? _avatar.transform : transform;
        }

        private void LateUpdate()
        {
            if (_avatar == null || _headBone == null)
            {
                WarnOnce();
                return;
            }

            // Owner-write replicated state is UNTRUSTED input — a buggy/spoofing owner could publish NaN/Inf,
            // which is sticky through the ease and would permanently corrupt the head (and its child bones).
            float _targetYaw = Sanitize(_avatar.SeatedYaw.Value);
            float _targetPitch = Sanitize(_avatar.SeatedPitch.Value);

            if (!_seeded)
            {
                // First frame: snap to the live look so a late-joining viewer (avatar already mid-look) does
                // not see the head visibly swing in from 0.
                _displayedYaw = _targetYaw;
                _displayedPitch = _targetPitch;
                _seeded = true;
            }
            else
            {
                // Ease the displayed aim ANGLES toward the networked head-look (relative to the body facing).
                _displayedYaw = AvatarLookMath.EaseAngle(_displayedYaw, _targetYaw, _aimEaseSpeed, Time.deltaTime);
                _displayedPitch = AvatarLookMath.EaseAngle(_displayedPitch, _targetPitch, _aimEaseSpeed, Time.deltaTime);
            }

            // Aim the freshly-animated bone by an intrinsic yaw-then-pitch in the BODY (root) frame — the SAME
            // Euler(pitch, yaw, 0) composition the eye pivot uses — expressed as a world-space delta so it
            // layers over the clip without coupling pitch onto a fixed world axis as yaw grows.
            Quaternion _aimInRoot = Quaternion.Euler(_displayedPitch, _displayedYaw, 0f);
            Quaternion _delta = _root.rotation * _aimInRoot * Quaternion.Inverse(_root.rotation);
            _headBone.rotation = _delta * _headBone.rotation;
        }

        private static float Sanitize(float _value) => float.IsFinite(_value) ? _value : 0f;

        private void WarnOnce()
        {
            if (_warned)
            {
                return;
            }
            _warned = true;
            Debug.LogWarning(
                "AvatarHeadLook: no PlayerAvatar or HeadBone wired — head aim disabled on this avatar.", this);
        }
    }
}
