using System.Threading;
using AudioSystem;
using Avatars;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using FMODUnity;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Presentation
{
    /// <summary>
    /// The chained cat's moment (board task T19, points 2 to 4; maquettes enchainement v2: E1 + S2 + F2 + Y1; the card
    /// that followed it is gone since the first-person day, T23). A camera in front of the chained cat, the same for everyone (its own player too), the room
    /// dark around it; the artist's « Getting Chained » animation plays while a force lifts the cat slowly, then lets
    /// it fall back on its seat: the sound is sucked out just before the splatch, and the flattest pose stays frozen
    /// half a second with the camera jumped in (<see cref="ChainRevealTimeline"/>).
    /// <para>Local presentation only: the animation itself is the networked one (the server sets the chained cat's
    /// Animator); every client follows the clip's own progress on its copy, so lag only delays the shot.</para>
    /// Lane A (scene-wired), played by <c>ChainingState</c> through <c>ChainingManager.RevealDirector</c>.
    /// </summary>
    public class ChainRevealDirector : MonoBehaviour
    {
        private const string GettingChained = "Getting Chained";
        private const string GettingChained2 = "Getting Chained 2";
        private const string ChainedIdle = "Chained Idle";

        [SerializeField] private CinemachineCamera _camera;
        [SerializeField] private AvatarVisibilityController _visibility;
        [SerializeField] private SeatedSurroundings _surroundings;
        [Tooltip("The day's screen rows (T23): they leave the screen during the shot. Optional.")]
        [SerializeField] private DayScreenRows _dayRows;
        [Tooltip("The light over the chained cat (the room is dark). Off outside the moment.")]
        [SerializeField] private Light _light;
        [SerializeField] private EventReference _liftSound;
        [SerializeField] private EventReference _splatSound;

        [Header("Camera (in cat heights, from its seat toward the table)")]
        [Tooltip("Height of the cat model at scale 1 (the avatar's scale multiplies it).")]
        [SerializeField] private float _catHeightAtScale1 = 4.4f;
        [SerializeField] private float _cameraBack = 2.2f;
        [SerializeField] private float _cameraSide = 0.5f;
        [SerializeField] private float _cameraUp = 1.8f;
        [SerializeField] private float _aimUp = 0.55f;
        [SerializeField] private float _aimForward = 0.41f;
        [SerializeField] private float _fieldOfView = 50f;
        [Tooltip("The camera's jump at the splatch (F2): the field of view shrinks by this share.")]
        [SerializeField] private float _punchIn = 0.12f;
        [SerializeField] private int _activePriority = 300;
        [SerializeField] private int _inactivePriority = -100;

        [Header("Timing")]
        [Tooltip("How long to wait for the cat's animation to start (the server sets it a moment after the state).")]
        [SerializeField] private float _maxWaitSeconds = 2f;
        [Tooltip("The shot stays on the chained cat this long after the freeze.")]
        [SerializeField] private float _afterSeconds = 1.2f;
        [SerializeField] private float _lightIntensity = 1800f;

        private void Awake()
        {
            Assert.IsNotNull(_camera, "ChainRevealDirector._camera not wired");
            Assert.IsNotNull(_visibility, "ChainRevealDirector._visibility not wired");
            _camera.Priority = _inactivePriority;
            if (_light != null)
            {
                _light.enabled = false;
            }
        }

        /// <summary>
        /// Plays the moment for the chained player of seat <paramref name="_seat"/>, and returns once the shot is over. Returns at once when that seat has no cat at the table (a bot, a player who left).
        /// </summary>
        public async UniTask PlayAsync(ulong _seat, CancellationToken _ct)
        {
            AvatarManager _manager = AvatarManager.For(NetworkManager.Singleton);
            PlayerAvatar _avatar = _manager != null ? _manager.GetAvatar(_seat) : null;
            Animator _animator = _avatar != null ? _avatar.GetComponentInChildren<Animator>() : null;
            if (_animator == null)
            {
                return;
            }
            AvatarHopMotion _hop = _avatar.GetComponentInChildren<AvatarHopMotion>();
            float _catHeight = _catHeightAtScale1 * _avatar.transform.lossyScale.y;

            Frame(_avatar.transform, _catHeight);
            _camera.Priority = _activePriority;
            _visibility.SetFeatured(_avatar);
            SetDark(true);
            if (_dayRows != null)
            {
                _dayRows.SetAway(true);
            }
            Debug.Log($"[CHAINREVEAL] seat {_seat}: shot on the chained cat");

            GameAudioManager _audio = GameAudioManager.instance;
            try
            {
                float _t = -1f;
                for (float _waited = 0f; _waited < _maxWaitSeconds; _waited += Time.deltaTime)
                {
                    if (TryClipTime(_animator, out _t))
                    {
                        break;
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, _ct);
                }
                if (_t < 0f || _t >= ChainRevealTimeline.Impact)
                {
                    // Already chained when the shot came (a late client) or no animation: just show it.
                    Debug.Log($"[CHAINREVEAL] seat {_seat}: animation {(_t < 0f ? "not seen" : "already past")}, plain shot");
                    await UniTask.Delay(System.TimeSpan.FromSeconds(_afterSeconds), cancellationToken: _ct);
                    return;
                }

                float _end = ChainRevealTimeline.Flat + ChainRevealTimeline.FreezeSeconds + _afterSeconds;
                bool _lifting = false, _splatched = false;
                while (_t < _end)
                {
                    if (!_lifting && _t >= ChainRevealTimeline.LiftStart)
                    {
                        _lifting = true;
                        _audio?.PlayOneShot(_liftSound);
                    }
                    if (_hop != null)
                    {
                        _hop.LiftWorld = ChainRevealTimeline.Lift(_t) * ChainRevealTimeline.LiftShare * _catHeight;
                    }
                    if (!_splatched && _t >= ChainRevealTimeline.Impact)
                    {
                        _splatched = true;
                        _audio?.ClearMuffle();
                        _audio?.PlayOneShot(_splatSound);
                        Debug.Log($"[CHAINREVEAL] seat {_seat}: splatch");
                    }
                    else if (!_splatched && _t >= ChainRevealTimeline.SuckStart)
                    {
                        _audio?.SetMuffle(ChainRevealTimeline.SuckVolume(_t), ChainRevealTimeline.SuckCutoff(_t));
                    }
                    _audio?.SetBusVolume("bus:/Music", ChainRevealTimeline.MusicVolume(_t));
                    _animator.speed = ChainRevealTimeline.IsFrozen(_t) ? 0f : 1f;
                    _camera.Lens.FieldOfView = _fieldOfView * (1f - _punchIn * ChainRevealTimeline.PunchIn(_t));

                    await UniTask.Yield(PlayerLoopTiming.Update, _ct);
                    _t += Time.deltaTime;
                }
            }
            finally
            {
                if (_animator != null)
                {
                    _animator.speed = 1f;
                }
                if (_hop != null)
                {
                    _hop.LiftWorld = 0f;
                }
                _audio?.ClearMuffle();
                _audio?.SetBusVolume("bus:/Music", 1f);
                // Back to the board in a cut: a blend from here swings the camera through the room, lit again.
                CinemachineBrain _brain = Camera.main != null ? Camera.main.GetComponent<CinemachineBrain>() : null;
                CinemachineBlendDefinition _blend = _brain != null ? _brain.DefaultBlend : default;
                if (_brain != null)
                {
                    _brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
                }
                _camera.Priority = _inactivePriority;
                _camera.Lens.FieldOfView = _fieldOfView;
                _visibility.SetFeatured(null);
                SetDark(false);
                if (_dayRows != null)
                {
                    _dayRows.SetAway(false);
                }
                if (_brain != null)
                {
                    RestoreBlendNextFrame(_brain, _blend).Forget();
                }
            }
        }

        private static async UniTaskVoid RestoreBlendNextFrame(CinemachineBrain _brain, CinemachineBlendDefinition _blend)
        {
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, _brain.GetCancellationTokenOnDestroy());
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, _brain.GetCancellationTokenOnDestroy());
            _brain.DefaultBlend = _blend;
        }

        // Seconds since « Getting Chained » started, from the Animator (the next state while it cross-fades into it).
        private static bool TryClipTime(Animator _animator, out float _t)
        {
            AnimatorStateInfo _info = _animator.IsInTransition(0)
                ? _animator.GetNextAnimatorStateInfo(0)
                : _animator.GetCurrentAnimatorStateInfo(0);
            if (_info.IsName(GettingChained))
            {
                _t = _info.normalizedTime * _info.length;
                return true;
            }
            if (_info.IsName(GettingChained2))
            {
                // Its predecessor's length, from the clip itself (0.708 s in the artist's file).
                _t = FirstClipLength(_animator) + _info.normalizedTime * _info.length;
                return true;
            }
            if (_info.IsName(ChainedIdle))
            {
                _t = float.MaxValue;
                return true;
            }
            _t = -1f;
            return false;
        }

        private static float FirstClipLength(Animator _animator)
        {
            RuntimeAnimatorController _controller = _animator.runtimeAnimatorController;
            if (_controller != null)
            {
                foreach (AnimationClip _clip in _controller.animationClips)
                {
                    if (_clip != null && _clip.name == GettingChained)
                    {
                        return _clip.length;
                    }
                }
            }
            return 0.708f;
        }

        // In front of the cat, a little to the side and above, looking at its body (room for the lift above it).
        private void Frame(Transform _cat, float _catHeight)
        {
            Vector3 _seat = _cat.position;
            Vector3 _toTable = _cat.forward;
            _toTable.y = 0f;
            _toTable = _toTable.sqrMagnitude > 1e-4f ? _toTable.normalized : Vector3.forward;
            Vector3 _side = Vector3.Cross(Vector3.up, _toTable);
            Vector3 _eye = _seat + (_toTable * _cameraBack + _side * _cameraSide + Vector3.up * _cameraUp) * _catHeight;
            Vector3 _aim = _seat + (Vector3.up * _aimUp + _toTable * _aimForward) * _catHeight;
            _camera.transform.SetPositionAndRotation(_eye, Quaternion.LookRotation(_aim - _eye, Vector3.up));
            _camera.Lens.FieldOfView = _fieldOfView;
            if (_light != null)
            {
                _light.transform.position = _seat + (Vector3.up * 2.2f + _toTable * 1.7f) * _catHeight;
                _light.transform.LookAt(_seat + Vector3.up * (0.3f * _catHeight));
                _light.range = 5f * _catHeight;
            }
        }

        private void SetDark(bool _dark)
        {
            if (_surroundings != null)
            {
                _surroundings.ForceBlack = _dark;
                _surroundings.Apply();
            }
            if (_light != null)
            {
                _light.intensity = _lightIntensity;
                _light.enabled = _dark;
            }
        }
    }
}
