using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// The accusing arm points where its player looks (T3, Wouh 2026-11-05: "la tête tourne mais pas le bras").
    /// LOCAL presentation on every client, like <see cref="AvatarHeadLook"/>: each LateUpdate, after the Animator and
    /// the head look, while the cat plays the accusation (Animator states tagged <see cref="AccuseTag"/>: Accusing,
    /// Accused Idle), the pointing arm's shoulder bone turns by the avatar's networked look
    /// (<see cref="PlayerAvatar.SeatedYaw"/> / <see cref="PlayerAvatar.SeatedPitch"/>, relative to the body), the
    /// same yaw-then-pitch the head takes. The clip's arm points straight ahead: with a look of 0 nothing changes.
    ///
    /// The turn fades in and out with the pose (no snap when the arm rises or drops), and the yaw across the body is
    /// held tighter than the yaw away from it, so the paw never sweeps through the face. Reads replicated state,
    /// writes one bone: no game state, no RPC. A model without a PlayerAvatar parent (menu cat) no-ops.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public class AvatarArmAim : MonoBehaviour
    {
        /// <summary>Animator tag of the accusation states (Accusing, Accused Idle).</summary>
        public const string AccuseTag = "Angry";

        [Tooltip("Shoulder bone of the arm the accusation points with (Arm1.L: the artist's Accusing raises the left one).")]
        [SerializeField] private Transform _armBone;

        [Tooltip("Animator of this model (its current state's tag says whether the cat accuses).")]
        [SerializeField] private Animator _animator;

        [Tooltip("Largest turn (degrees) of the arm toward the other side of the body (the paw would cross the face).")]
        [SerializeField] private float _yawAcrossBody = 40f;

        [Tooltip("Largest turn (degrees) of the arm away from the body (the seated look reaches ±75°).")]
        [SerializeField] private float _yawAwayFromBody = 75f;

        [Tooltip("Largest pitch (degrees) of the arm, up or down (the seated look reaches ±40°).")]
        [SerializeField] private float _pitchLimit = 40f;

        [Tooltip("How fast the arm follows the look (same feel as the head look).")]
        [SerializeField] private float _aimEaseSpeed = 12f;

        [Tooltip("Seconds for the aim to fade in when the accusation starts / out when it ends.")]
        [SerializeField] private float _fadeSeconds = 0.35f;

        private PlayerAvatar _avatar;
        private Transform _root;
        // +1 when a positive yaw (turn toward +x of the body) swings the arm across the body: the arm hangs on -x.
        private float _acrossSign = 1f;
        private float _displayedYaw;
        private float _displayedPitch;
        private float _weight;
        private bool _aiming;

        private void Awake()
        {
            _avatar = GetComponentInParent<PlayerAvatar>();
            _root = _avatar != null ? _avatar.transform : transform;
            if (_animator == null) _animator = GetComponent<Animator>();
            if (_armBone != null)
            {
                _acrossSign = transform.InverseTransformPoint(_armBone.position).x < 0f ? 1f : -1f;
            }
        }

        private void OnEnable()
        {
            _weight = 0f;
            _aiming = false;
        }

        private void LateUpdate()
        {
            if (_avatar == null || _armBone == null || _animator == null)
            {
                return;
            }

            bool _accusing = IsAccusing();
            if (_accusing != _aiming)
            {
                _aiming = _accusing;
                Diagnostics.Logs.GameLog.Info("avatar", _accusing ? "avatar.arm-aim.start" : "avatar.arm-aim.stop",
                    "seat", _avatar.ownerClientId.Value, "yaw", _avatar.SeatedYaw.Value, "pitch", _avatar.SeatedPitch.Value);
            }
            _weight = Mathf.MoveTowards(_weight, _accusing ? 1f : 0f, _fadeSeconds <= 0f ? 1f : Time.deltaTime / _fadeSeconds);
            if (_weight <= 0f)
            {
                return;
            }

            float _targetYaw = ClampYaw(Sanitize(_avatar.SeatedYaw.Value), _acrossSign, _yawAcrossBody, _yawAwayFromBody);
            float _targetPitch = Mathf.Clamp(Sanitize(_avatar.SeatedPitch.Value), -_pitchLimit, _pitchLimit);
            _displayedYaw = AvatarLookMath.EaseAngle(_displayedYaw, _targetYaw, _aimEaseSpeed, Time.deltaTime);
            _displayedPitch = AvatarLookMath.EaseAngle(_displayedPitch, _targetPitch, _aimEaseSpeed, Time.deltaTime);

            Aim(_armBone, _root, _displayedYaw * _weight, _displayedPitch * _weight);
        }

        // The current state, or the one being blended into, is an accusation.
        private bool IsAccusing() =>
            _animator.GetCurrentAnimatorStateInfo(0).IsTag(AccuseTag)
            || (_animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).IsTag(AccuseTag));

        /// <summary>Turns <paramref name="bone"/> (on its own pivot) by a yaw then a pitch in the body frame of
        /// <paramref name="body"/>: the same composition as the head look.</summary>
        public static void Aim(Transform bone, Transform body, float yaw, float pitch)
        {
            Quaternion _aimInBody = Quaternion.Euler(pitch, yaw, 0f);
            Quaternion _delta = body.rotation * _aimInBody * Quaternion.Inverse(body.rotation);
            bone.rotation = _delta * bone.rotation;
        }

        /// <summary>The look's yaw held to the arm's reach: <paramref name="across"/> toward the other side of the
        /// body (the side <paramref name="acrossSign"/> points to), <paramref name="away"/> the other way.</summary>
        public static float ClampYaw(float yaw, float acrossSign, float across, float away)
        {
            float _signed = yaw * acrossSign;
            return Mathf.Clamp(_signed, -away, across) * acrossSign;
        }

        private static float Sanitize(float _value) => float.IsFinite(_value) ? _value : 0f;
    }
}
