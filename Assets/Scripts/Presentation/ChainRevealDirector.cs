using System.Threading;
using AudioSystem;
using Avatars;
using Board;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using FMODUnity;
using Presentation.Chains;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Presentation
{
    /// <summary>
    /// The chained cat's moment, seen seated by everyone, its own player too (board task T19, maquettes enchainement
    /// v4: D2 + Y1, Poyo 2026-10-21). A force lifts the cat slowly while carrying it to the middle of the circle; on each
    /// screen it turns to face that player. Its real card leaves the row at the bottom of the screen, comes in front of
    /// it and turns over on its role; the card goes back, the cat falls to the floor, its flattest pose held half a
    /// second, it gets up, and under the day's black (<see cref="DayTableTransition"/>) it is back on its seat. The room
    /// goes dark around it, a light follows it. Timings: <see cref="ChainRevealTimeline"/>.
    /// <para>Local presentation only, the same on every client from its own seat: the cat's place is computed from this
    /// player's eye (the middle of the circle, pushed back until it fits the screen), and its pose is sampled locally
    /// from the artist's clips, so the server's own chained animation (the Animator) is simply covered meanwhile.</para>
    /// Lane A (scene-wired), played by <c>ChainingState</c> through <c>ChainingManager.RevealDirector</c>.
    /// </summary>
    [DefaultExecutionOrder(210)] // after the hop (160) and the head look (200): the sampled pose is the last word
    public class ChainRevealDirector : MonoBehaviour
    {
        private const string GettingChained = "Getting Chained";
        private const string GettingChained2 = "Getting Chained 2";
        private const string ChainedIdle = "Chained Idle";

        [SerializeField] private AvatarVisibilityController _visibility;
        [Tooltip("The day's rows (T23): the chained player's card leaves its row for the moment.")]
        [SerializeField] private DayScreenRows _dayRows;
        [Tooltip("The day's table transition: the moment waits for the table to be gone, and ends under its black.")]
        [SerializeField] private DayTableTransition _day;
        [SerializeField] private CatSpotlights _spotlights;
        [Tooltip("The light that follows the chained cat (the room is dark). Off outside the moment.")]
        [SerializeField] private Light _light;
        [SerializeField] private EventReference _liftSound;
        [SerializeField] private EventReference _splatSound;

        [Header("Where it floats (in cat heights)")]
        [Tooltip("Height of the cat model at scale 1 (the avatar's scale multiplies it).")]
        [SerializeField] private float _catHeightAtScale1 = 4.4f;
        [Tooltip("Its feet this far above the floor once lifted.")]
        [SerializeField] private float _liftShare = 0.6f;
        [Tooltip("A seated cat's height, for a seat with no cat at the table (a bot): its card alone.")]
        [SerializeField] private float _fallbackCatHeight = 14.85f;

        [Header("Its card")]
        [SerializeField] private float _cardHeightShare = 0.46f;
        [Tooltip("The card's centre above the cat's feet: held against its torso, under its head (Poyo 2026-10-22).")]
        [SerializeField] private float _cardUpShare = 0.22f;
        [Tooltip("The card's centre in front of the cat, toward the player: clear of the whole cat (its big head comes down to " +
                 "the card and reaches 0.38 of a cat's height forward in the chained pose, measured), so it never clips.")]
        [SerializeField] private float _cardForwardShare = 0.42f;

        [Header("The dark")]
        [Tooltip("The other cats' lights at the darkest (share of their light).")]
        [SerializeField] private float _darkenTo = 0.2f;
        [SerializeField] private float _lightIntensity = 2600f;
        [Tooltip("How long to wait for the table to be gone (the morning chaining starts the day).")]
        [SerializeField] private float _maxWaitSeconds = 5f;

        // The pose sampled every LateUpdate while the moment plays (after the Animator, the hop and the head look).
        private Animator _animator;
        private AnimationClip _clip1;
        private AnimationClip _clip2;
        private AnimationClip _idleClip;
        private float _clipTime = -1f;
        // Its chains, appearing on it with the pose (maquettes chaines v2: right paw, left paw, then its body).
        private CatChains _chains;

        private void Awake()
        {
            Assert.IsNotNull(_visibility, "ChainRevealDirector._visibility not wired");
            Assert.IsNotNull(_dayRows, "ChainRevealDirector._dayRows not wired");
            Assert.IsNotNull(_day, "ChainRevealDirector._day not wired");
            Assert.IsNotNull(_spotlights, "ChainRevealDirector._spotlights not wired");
            if (_light != null)
            {
                _light.enabled = false;
            }
        }

        /// <summary>
        /// Plays the moment for the chained player of seat <paramref name="_seat"/>. A seat with no cat at the table (a
        /// bot) shows its card alone in the middle.
        /// </summary>
        public async UniTask PlayAsync(ulong _seat, CancellationToken _ct)
        {
            // The morning chaining starts the day: the cards fly to the screen and the table goes first.
            for (float _waited = 0f; !_day.DaySettled && _waited < _maxWaitSeconds; _waited += Time.unscaledDeltaTime)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, _ct);
            }
            if (!_day.DaySettled)
            {
                Diagnostics.Logs.GameLog.Warn("chain", "chain.reveal.wait.timeout", "seat", _seat, "seconds", _maxWaitSeconds);
            }

            Camera _camera = Camera.main;
            AvatarManager _manager = AvatarManager.For(NetworkManager.Singleton);
            if (_camera == null || _manager == null)
            {
                Diagnostics.Logs.GameLog.Warn("chain", "chain.reveal.skipped", "seat", _seat, "reason", _camera == null ? "no-camera" : "no-avatars");
                return;
            }
            PlayerAvatar _avatar = _manager.GetAvatar(_seat);
            AvatarHopMotion _hop = _avatar != null ? _avatar.GetComponentInChildren<AvatarHopMotion>() : null;
            bool _self = _avatar != null && _avatar.IsOwner;

            // ---- where: the middle of the circle (the day's circle is sized for it, Poyo 2026-10-21); still around the
            // table, on the line from this player's eye to the middle, as far as it needs to fit
            Vector3 _eye = _camera.transform.position;
            Vector3 _centre = _manager.RingCenter ?? _eye + _camera.transform.forward * 20f;
            Vector3 _toCentre = _centre - _eye;
            _toCentre.y = 0f;
            float _ringDistance = _toCentre.magnitude;
            Vector3 _ahead = _ringDistance > 1e-3f ? _toCentre / _ringDistance : Vector3.forward;
            // The player's own cat is drawn bigger than the others (its seat is nearer the middle): it flies at their size.
            float _scale = _self ? _manager.SeatedAvatarScale : 1f;
            float _catHeight = _avatar != null ? _catHeightAtScale1 * _avatar.transform.lossyScale.y * _scale : _fallbackCatHeight;
            float _distance = _manager.DayRingActive
                ? _ringDistance
                : ChainRevealTimeline.HoverDistance(_ringDistance, _catHeight, _camera.fieldOfView, _manager.DayScreenShare);
            float _floor = _avatar != null ? _avatar.transform.position.y : _centre.y;
            Vector3 _hover = new Vector3(_eye.x, _floor, _eye.z) + _ahead * _distance;
            Quaternion _facing = Quaternion.LookRotation(-_ahead, Vector3.up);
            Vector3 _seatPosition = _avatar != null ? _avatar.transform.position : _hover;
            Quaternion _seatRotation = _avatar != null ? _avatar.transform.rotation : _facing;
            float _lift = _liftShare * _catHeight;

            // ---- its card, out of its row (the roles' row goes down first: its slot is where the cards' row is going)
            _dayRows.SetRolesAway(true);
            Card _card = _dayRows.CardOf(_seat);
            bool _hasCard = _card != null && _dayRows.BeginSpotlight(_seat);
            Vector2 _slotCentre = Vector2.zero;
            float _slotHeight = 0f;
            _hasCard = _hasCard && _dayRows.TryGetSlot(_seat, out _slotCentre, out _slotHeight);
            bool _revealed = _self; // its own player knows its role already: the card comes without turning over

            float _end = _avatar != null ? ChainRevealTimeline.BlackOut : ChainRevealTimeline.CardBack;
            Debug.Log($"[CHAINREVEAL] seat {_seat}: {(_avatar != null ? "cat" : "card only")}{(_self ? " (mine)" : "")}, {_distance:F1} m away");
            Diagnostics.Logs.GameLog.Info("chain", "chain.reveal.start", "seat", _seat, "cat", _avatar != null, "mine", _self, "card", _hasCard);
            GameAudioManager _audio = GameAudioManager.instance;
            bool _completed = false;
            try
            {
                if (_avatar != null)
                {
                    BeginCat(_avatar, _self);
                    _audio?.PlayOneShot(_liftSound);
                }
                bool _splatched = false;
                for (float _t = 0f; _t < _end; _t += Time.deltaTime)
                {
                    float _travel = ChainRevealTimeline.Travel(_t);
                    Vector3 _feet = Vector3.Lerp(_seatPosition, _hover, _travel);
                    _feet.y = _floor + ChainRevealTimeline.Height(_t) * _lift;
                    if (_hop != null)
                    {
                        _hop.SetWorldPose(_feet, Quaternion.Slerp(_seatRotation, _facing, _travel), Mathf.Lerp(1f, _scale, _travel));
                    }
                    _clipTime = ChainRevealTimeline.ClipTime(_t);
                    Darken(ChainRevealTimeline.Darkness(_t), _feet, _catHeight);

                    if (_hasCard && _t >= ChainRevealTimeline.CardBack)
                    {
                        // Back in its row: it goes down with the rows while the cat falls.
                        _hasCard = false;
                        _dayRows.EndSpotlight();
                    }
                    if (_hasCard)
                    {
                        Vector3 _anchor = _hover + Vector3.up * (_cardUpShare * _catHeight + (_feet.y - _floor)) - _ahead * (_cardForwardShare * _catHeight);
                        PlaceCard(_camera, _anchor, _cardHeightShare * _catHeight, ChainRevealTimeline.CardFlight(_t), _slotCentre, _slotHeight);
                    }
                    if (!_revealed && _t >= ChainRevealTimeline.CardArrived)
                    {
                        _revealed = true;
                        if (_card != null)
                        {
                            _ = _card.ShowPseudoWithRevealedInfo(true);
                        }
                        Diagnostics.Logs.GameLog.Info("chain", "chain.reveal.role", "seat", _seat, "card", _card != null);
                    }
                    // The rows go down while it falls: it lands where they would hide it (the head stays free).
                    if (_avatar != null && _t >= ChainRevealTimeline.Release)
                    {
                        _dayRows.SetAway(true);
                    }
                    if (!_splatched && _avatar != null && _t >= ChainRevealTimeline.Impact)
                    {
                        _splatched = true;
                        _audio?.PlayOneShot(_splatSound);
                        Debug.Log($"[CHAINREVEAL] seat {_seat}: splatch");
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, _ct);
                }
                if (_avatar != null)
                {
                    await _day.BlackOutAsync(() => EndCat(_hop, _seat), _ct);
                }
                _completed = true;
            }
            finally
            {
                Diagnostics.Logs.GameLog.Info("chain", "chain.reveal.end", "seat", _seat, "completed", _completed, "revealed", _revealed);
                EndCat(_hop, _seat);
                if (_hasCard)
                {
                    _dayRows.EndSpotlight();
                }
                if (!_revealed && _card != null)
                {
                    _ = _card.ShowPseudoWithRevealedInfo(false);
                }
            }
        }

        private void BeginCat(PlayerAvatar _avatar, bool _self)
        {
            _animator = _avatar.GetComponentInChildren<Animator>();
            _clip1 = _clip2 = _idleClip = null;
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (AnimationClip _clip in _animator.runtimeAnimatorController.animationClips)
                {
                    if (_clip == null) continue;
                    if (_clip.name == GettingChained) _clip1 = _clip;
                    else if (_clip.name == GettingChained2) _clip2 = _clip;
                    else if (_clip.name == ChainedIdle) _idleClip = _clip;
                }
            }
            // The player's own body is hidden in first person: this once, it is seen leaving.
            if (_self)
            {
                _visibility.SetFeatured(_avatar);
            }
            _spotlights.Lifted = _avatar;
            _chains = _avatar.GetComponentInChildren<CatChains>();
            if (_chains != null)
            {
                _chains.BeginReveal();
            }
        }

        // Back on its seat (idempotent: under the black, then again in finally).
        private void EndCat(AvatarHopMotion _hop, ulong _seat)
        {
            if (_hop != null)
            {
                _hop.ClearWorldPose();
            }
            if (_clipTime >= 0f)
            {
                Debug.Log($"[CHAINREVEAL] seat {_seat}: back on its seat");
            }
            _clipTime = -1f;
            _animator = null;
            if (_chains != null)
            {
                _chains.EndReveal(); // it keeps them, even if the moment was cut short
                _chains = null;
            }
            _visibility.SetFeatured(null);
            _dayRows.SetAway(false);
            _dayRows.SetRolesAway(false);
            _spotlights.Lifted = null;
            _spotlights.Darken = 1f;
            if (_light != null)
            {
                _light.enabled = false;
            }
        }

        // The room's lights go down, one light stays on the cat (above it, on the player's side).
        private void Darken(float _darkness, Vector3 _feet, float _catHeight)
        {
            _spotlights.Darken = Mathf.Lerp(1f, _darkenTo, _darkness);
            if (_light == null)
            {
                return;
            }
            Camera _camera = Camera.main;
            Vector3 _toEye = _camera != null ? _camera.transform.position - _feet : Vector3.back;
            _toEye.y = 0f;
            _toEye = _toEye.sqrMagnitude > 1e-4f ? _toEye.normalized : Vector3.back;
            Vector3 _middle = _feet + Vector3.up * (0.5f * _catHeight);
            Vector3 _from = _middle + Vector3.up * (1.6f * _catHeight) + _toEye * (1.2f * _catHeight);
            _light.transform.SetPositionAndRotation(_from, Quaternion.LookRotation(_middle - _from, Vector3.up));
            _light.range = 6f * _catHeight;
            _light.intensity = _lightIntensity * _darkness;
            _light.enabled = _darkness > 0f;
        }

        // The card between its row slot (flight 0) and the cat's torso (flight 1). On screen it slides and grows evenly
        // (screen half-heights) while it goes from the rows' depth into the scene (evenly in log depth): at the end it is
        // a real card in front of the cat, in its light (Poyo 2026-10-22), and it comes back the same way.
        private void PlaceCard(Camera _camera, Vector3 _anchor, float _worldHeight, float _flight, Vector2 _slotCentre, float _slotHeight)
        {
            float _rowDepth = Mathf.Max(0.01f, _dayRows.RowDistance);
            float _tan = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 _view = _camera.WorldToViewportPoint(_anchor);
            Vector2 _target = _slotCentre;
            float _targetHeight = _slotHeight;
            float _targetDepth = _rowDepth;
            if (_view.z > _rowDepth)
            {
                _target = new Vector2((_view.x * 2f - 1f) * _camera.aspect, _view.y * 2f - 1f);
                _targetHeight = _worldHeight / (_view.z * _tan);
                _targetDepth = _view.z;
            }
            Vector2 _centre = Vector2.LerpUnclamped(_slotCentre, _target, _flight);
            float _height = Mathf.LerpUnclamped(_slotHeight, _targetHeight, _flight);
            float _depth = _rowDepth * Mathf.Pow(_targetDepth / _rowDepth, Mathf.Clamp01(_flight));
            Vector3 _world = _camera.ViewportToWorldPoint(new Vector3((_centre.x / _camera.aspect + 1f) * 0.5f, (_centre.y + 1f) * 0.5f, _depth));
            _dayRows.SetSpotlightWorldPose(_world, _height * _depth * _tan);
        }

        private void LateUpdate()
        {
            if (_clipTime < 0f || _animator == null)
            {
                return;
            }
            GameObject _target = _animator.gameObject;
            if (_clipTime < ChainRevealTimeline.FirstClipLength)
            {
                if (_clip1 != null) _clip1.SampleAnimation(_target, _clipTime);
            }
            else if (_clipTime < ChainRevealTimeline.ClipEnd)
            {
                if (_clip2 != null) _clip2.SampleAnimation(_target, _clipTime - ChainRevealTimeline.FirstClipLength);
            }
            else if (_idleClip != null)
            {
                _idleClip.SampleAnimation(_target, Mathf.Repeat(_clipTime - ChainRevealTimeline.ClipEnd, _idleClip.length));
            }
            if (_chains != null)
            {
                _chains.SetRevealTime(_clipTime);
            }
        }
    }
}
