using Extensions;
using FMODUnity;
using UnityEngine;

namespace Avatars
{
    /// <summary>
    /// The cat's voice on its emotes (T3, Wouh 2026-11-05: "des bruitages lorsqu'ils emote, des miaulements"). LOCAL
    /// presentation on every client, like <see cref="Cosmetics.CatTears"/>: the emotes replicate through the body's
    /// NetworkAnimator, so each peer watches its own copy of the Animator and voices the state it enters: Coucou (Wave,
    /// WaveLoop) meows, Pleurer (states tagged Crying, chained or not) sobs and sobs again while it lasts, Accuser
    /// (Accusing) hisses. The chained struggle stays silent (Poyo 2026-11-18: it repeats on its own, a cry each time
    /// would be unbearable). One voice per entry: the clips loop in well
    /// under a second, a sound per loop would never stop.
    ///
    /// After a public vote every voter accuses at once: each accusation waits a short random delay, so the table does
    /// not hiss in one block. Only an in-game cat speaks (a PlayerAvatar above it): the menu's cat stays silent.
    /// </summary>
    public class CatVoice : MonoBehaviour
    {
        private const string CryingTag = "Crying";
        private static readonly int Wave = Animator.StringToHash("Wave");
        private static readonly int WaveLoop = Animator.StringToHash("WaveLoop");
        private static readonly int Accusing = Animator.StringToHash("Accusing");

        [SerializeField] private Animator animator;

        [Tooltip("Coucou: a meow when the paw rises.")]
        [SerializeField] private EventReference meow;

        [Tooltip("Pleurer: a sob when the cat starts crying, and again every cryEverySeconds while it cries.")]
        [SerializeField] private EventReference cry;

        [Tooltip("Accuser: when the paw points.")]
        [SerializeField] private EventReference accuse;

        [Tooltip("Seconds between two sobs while the cat keeps crying (0 = only the first). ±0.5 s at random.")]
        [SerializeField] private float cryEverySeconds = 4f;

        [Tooltip("Largest random wait before an accusation's voice (several cats accuse together after a public vote).")]
        [SerializeField] private float accuseMaxDelay = 0.35f;

        private PlayerAvatar _avatar;
        private int _state;
        private bool _crying;
        private float _nextCry;
        private float _accuseAt = -1f;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            _avatar = GetComponentInParent<PlayerAvatar>();
        }

        private void OnDisable()
        {
            _state = 0;
            _crying = false;
            _accuseAt = -1f;
        }

        private void Update()
        {
            if (_avatar == null || animator == null || !animator.isActiveAndEnabled)
            {
                return;
            }

            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            int state = info.shortNameHash;
            if (state != _state)
            {
                bool wasWaving = _state == Wave || _state == WaveLoop;
                _state = state;
                if ((state == Wave || state == WaveLoop) && !wasWaving)
                {
                    Say(meow, "meow");
                }
                else if (state == Accusing)
                {
                    _accuseAt = Time.time + Random.Range(0f, accuseMaxDelay);
                }
            }

            bool crying = info.IsTag(CryingTag);
            if (crying && (!_crying || (cryEverySeconds > 0f && Time.time >= _nextCry)))
            {
                Say(cry, _crying ? "cry-again" : "cry");
                _nextCry = Time.time + cryEverySeconds + Random.Range(-0.5f, 0.5f);
            }
            _crying = crying;

            if (_accuseAt >= 0f && Time.time >= _accuseAt)
            {
                _accuseAt = -1f;
                Say(accuse, "accuse");
            }
        }

        private void Say(EventReference voice, string what)
        {
            Diagnostics.Logs.GameLog.Info("avatar", "avatar.voice", "seat", _avatar.ownerClientId.Value, "voice", what,
                "event", voice.IsNull ? "none" : voice.GetPath());
            voice.TryPlayOneShot();
        }
    }
}
