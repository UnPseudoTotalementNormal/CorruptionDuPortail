using CorruptionDuPortail.Domain;
using UnityEngine;

namespace Avatars.Cosmetics
{
    /// <summary>
    /// The cat of a player who speaks (T22, mockups vocal v2): a black mouth opens on its muzzle with his voice, as in
    /// Peak (W3, R1 with a little fidget), the mouth he picked hides meanwhile (M1), and his head sways a little when
    /// he speaks loud (W3). Nothing else on screen (X1). Driven by <see cref="PlayerAvatar.VoiceLevel"/>, which each
    /// listener sets from the voice it plays: presentation only, nothing replicated. A standalone model (menu) no-ops.
    /// Runs after <see cref="AvatarHeadLook"/>, which aims the freshly animated head bone first.
    /// </summary>
    [DefaultExecutionOrder(210)]
    [RequireComponent(typeof(CatAppearance))]
    public class CatTalk : MonoBehaviour
    {
        // Below this the talking mouth is hidden.
        private const float ShutBelow = 0.03f;

        [Tooltip("The black talking mouth on the muzzle (child of the head bone, inactive). Its scale is the fully open one.")]
        [SerializeField] private Transform mouth;

        [Tooltip("The rigged head bone, swayed while speaking.")]
        [SerializeField] private Transform headBone;

        [Tooltip("Share of the mouth's size that keeps wobbling while it is open (0 = follows the voice only).")]
        [Range(0f, 1f)] [SerializeField] private float fidget = 0.25f;

        [Tooltip("Seconds for the mouth to open / to close.")]
        [SerializeField] private float openSeconds = 0.03f;
        [SerializeField] private float closeSeconds = 0.08f;

        [Tooltip("Seconds the worn mouth stays hidden after the voice stops (a pause between two words keeps the black one).")]
        [SerializeField] private float holdSeconds = 0.4f;

        [Tooltip("Head sway at full voice (degrees): roll side to side, nod.")]
        [SerializeField] private float rollDegrees = 4f;
        [SerializeField] private float nodDegrees = 3f;

        private CatAppearance _appearance;
        private PlayerAvatar _avatar;
        private Vector3 _openScale;
        private float _open;
        private float _lastVoice = float.NegativeInfinity;

        private void Awake()
        {
            _appearance = GetComponent<CatAppearance>();
            _avatar = GetComponentInParent<PlayerAvatar>();
            if (mouth != null)
            {
                _openScale = mouth.localScale;
                mouth.gameObject.SetActive(false);
            }
            else if (_avatar != null)
            {
                Diagnostics.Logs.GameLog.Warn("avatar", "avatar.talk.unwired", "object", name);
            }
        }

        private void OnDisable()
        {
            _open = 0f;
            if (mouth != null) mouth.gameObject.SetActive(false);
            if (_appearance != null) _appearance.SetMouthHidden(false);
        }

        private void LateUpdate()
        {
            if (_avatar == null || mouth == null) return;

            float target = Mathf.Clamp01(_avatar.VoiceLevel);
            float seconds = target > _open ? openSeconds : closeSeconds;
            _open = Mathf.MoveTowards(_open, target, Time.deltaTime / Mathf.Max(seconds, 0.001f));
            if (target > 0f) _lastVoice = Time.time;

            float scale = (float)VoiceLevelTrack.MouthScale(_open, Time.time, fidget);
            bool shown = scale > ShutBelow;
            if (mouth.gameObject.activeSelf != shown) mouth.gameObject.SetActive(shown);
            if (shown) mouth.localScale = _openScale * scale;
            _appearance.SetMouthHidden(shown || Time.time - _lastVoice < holdSeconds);

            if (headBone != null && _open > 0f)
            {
                Transform body = _avatar.transform;
                float roll = rollDegrees * _open * Mathf.Sin(Time.time * 6.7f);
                float nod = nodDegrees * _open * Mathf.Sin(Time.time * 9.3f + 1f);
                headBone.rotation = Quaternion.AngleAxis(roll, body.forward) * Quaternion.AngleAxis(nod, body.right)
                                    * headBone.rotation;
            }
        }
    }
}
