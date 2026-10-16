using UnityEngine;

namespace Avatars.Cosmetics
{
    /// <summary>
    /// The cat blinks now and then when its eyes have a closed variant (the black and white eyes: "BW Eyes Closed" of
    /// the artist's file). Purely cosmetic and local: every replica blinks on its own, nothing is networked.
    /// </summary>
    [RequireComponent(typeof(CatAppearance))]
    public class CatBlink : MonoBehaviour
    {
        [Tooltip("Seconds between two blinks (random in this range).")]
        [SerializeField] private Vector2 interval = new(2.5f, 6f);

        [Tooltip("How long the eyes stay closed (seconds).")]
        [SerializeField] private float closedSeconds = 0.12f;

        private CatAppearance _appearance;
        private float _next;
        private float _reopen = -1f;

        private void Awake()
        {
            _appearance = GetComponent<CatAppearance>();
            _next = Time.time + Random.Range(interval.x, interval.y);
        }

        private void Update()
        {
            if (_reopen >= 0f && Time.time >= _reopen)
            {
                _appearance.SetBlink(false);
                _reopen = -1f;
            }
            if (Time.time < _next) return;
            _next = Time.time + Random.Range(interval.x, interval.y);
            if (!_appearance.CanBlink) return;
            _appearance.SetBlink(true);
            _reopen = Time.time + closedSeconds;
        }

        private void OnDisable()
        {
            if (_appearance != null) _appearance.SetBlink(false);
            _reopen = -1f;
        }
    }
}
