using UnityEngine;

namespace Avatars.Cosmetics
{
    /// <summary>
    /// The eyes frown (their "Angry" shape key) while the Animator is in a state tagged "Angry" (Accusing, Accused
    /// Idle): the artist's note on the accusing animation asked for furrowed brows. Eased in and out; every peer runs
    /// it from the replicated Animator state. Only eyes with that shape key frown (the black and white ones).
    /// </summary>
    [RequireComponent(typeof(CatAppearance))]
    public class CatFrown : MonoBehaviour
    {
        private const string AngryTag = "Angry";

        [SerializeField] private Animator animator;

        [Tooltip("Seconds to frown fully / to relax.")]
        [SerializeField] private float easeSeconds = 0.2f;

        private CatAppearance _appearance;
        private float _weight;

        private void Awake() => _appearance = GetComponent<CatAppearance>();

        private void Update()
        {
            if (animator == null) return;
            float target = animator.GetCurrentAnimatorStateInfo(0).IsTag(AngryTag) ? 1f : 0f;
            if (Mathf.Approximately(target, _weight)) return;
            _weight = Mathf.MoveTowards(_weight, target, Time.deltaTime / Mathf.Max(easeSeconds, 0.01f));
            _appearance.SetAngry(_weight);
        }
    }
}
