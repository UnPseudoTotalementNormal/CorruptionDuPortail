#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Globalization;
using Avatars;
using Board.BoardCameraSystem;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using Presentation;
using UnityEngine;

namespace Autoplay
{
    /// <summary>
    /// Seated view surroundings (board task T11): lever <c>-autoplay-seated-black 0|1</c> turns the pitch-black
    /// surroundings (<see cref="SeatedSurroundings"/>) off or on for this process, and probe <c>-autoplay-pov-tour</c>
    /// captures the first vote's seated first-person view head at rest, turned left, right and up (the player turns
    /// the head with the mouse), room shown, pitch black without then with the light per cat (<see cref="CatSpotlights"/>), each followed by a slow head sweep left → right → up
    /// (film it with <c>-autoplay-record "pov\.sweep:10"</c>). Captures <c>pov-*.jpg</c>, journal <c>pov.sweep</c>,
    /// <c>pov.tour</c>.
    /// </summary>
    public sealed partial class AutoplayDriver
    {
        private bool seatedBlackApplied;
        private bool povTourDone;

        private void UpdateSurroundings(GameState _state)
        {
            if (!string.IsNullOrEmpty(options.seatedBlack) && !seatedBlackApplied)
            {
                SeatedSurroundings _lever = FindFirstObjectByType<SeatedSurroundings>();
                if (_lever != null)
                {
                    seatedBlackApplied = true;
                    _lever.Enabled = options.seatedBlack != "0";
                    _lever.Apply();
                    Journal.Record("seated.black", _lever.Enabled ? "1" : "0");
                }
            }
            if (options.povTour && !povTourDone && _state is VoteState _vote)
            {
                povTourDone = true;
                inputBusy = true; // at once: the bots vote within seconds and an early full vote ends the phase
                _vote.voteTimer = Mathf.Max(_vote.voteTimer, 60f);
                StartCoroutine(PovTour());
            }
        }

        // Client side of -autoplay-pov-gaze-host: the head turned toward the host's cat (seat 0), held while the host tours.
        private IEnumerator GazeAtHost(VoteState _vote, AvatarEmbodiedCamera _camera)
        {
            AvatarManager _avatars = FindFirstObjectByType<AvatarManager>();
            Camera _eye = Camera.main;
            if (_avatars == null || _eye == null)
            {
                inputBusy = false;
                Journal.Record("pov.error", $"gaze-host: avatars={_avatars != null} camera={_eye != null}");
                yield break;
            }
            SeatPose _mine = _avatars.GetLocalSeatPose();
            SeatPose _host = _avatars.GetSeatPose(Unity.Netcode.NetworkManager.ServerClientId);
            Vector3 _to = _host.Position - _mine.Position;
            _to.y = 0f;
            Vector3 _forward = _mine.Rotation * Vector3.forward;
            _forward.y = 0f;
            float _yaw = Vector3.SignedAngle(_forward, _to, Vector3.up);
            SetEmbodiedLook(_yaw, 0f);
            Journal.Record("pov.gaze-host", string.Format(CultureInfo.InvariantCulture, "yaw={0:F1} distance={1:F1}", _yaw, _to.magnitude));
            for (float _t = 0f; _t < 120f && CurrentState is VoteState; _t += 1f)
            {
                _vote.voteTimer = Mathf.Max(_vote.voteTimer, 30f);
                SetEmbodiedLook(_yaw, 0f);
                yield return new WaitForSecondsRealtime(1f);
            }
            inputBusy = false;
        }

        private IEnumerator PovTour()
        {
            yield return new WaitForSecondsRealtime(4f); // cards shown, panels out
            VoteState _vote = CurrentState as VoteState;
            BoardCameraManager _views = BoardCameraManager.instance;
            SeatedSurroundings _surroundings = FindFirstObjectByType<SeatedSurroundings>();
            CatSpotlights _catLights = FindFirstObjectByType<CatSpotlights>();
            AvatarEmbodiedCamera _camera = FindFirstObjectByType<AvatarEmbodiedCamera>();
            if (_vote == null || _views == null || _surroundings == null || _catLights == null || _camera == null)
            {
                inputBusy = false;
                Journal.Record("pov.error", $"vote={_vote != null} views={_views != null} surroundings={_surroundings != null} catLights={_catLights != null} camera={_camera != null}");
                yield break;
            }
            _views.SetCurrentBoardCamera(BoardCameraIdEnum.SeatedFirstPerson);
            yield return new WaitForSecondsRealtime(1.5f);
            if (options.povGazeHost && Unity.Netcode.NetworkManager.Singleton != null && !Unity.Netcode.NetworkManager.Singleton.IsServer)
            {
                yield return GazeAtHost(_vote, _camera);
                yield break;
            }
            GameObject _standIns = standInCats ? SpawnStandInCats(_catLights) : null;
            bool _black = _surroundings.Enabled;
            bool _catLit = _catLights.Enabled;
            float _yawClamp = GetField<float>(_camera, "_yawClamp");
            float _rest = _camera.RestPitch;
            float _up = -Mathf.Min(_camera.PitchClamp, 25f);
            // Head half down: the floor ahead, where the vote candle stands by day (T23), above the day rows (T38).
            // Fully down (60 degrees) looks past it, at the floor under the player's own chair.
            float _down = _camera.PitchClamp * 0.5f;
            var _variants = new (string name, bool black, bool catLights)[] { ("room", false, true), ("black-nocatlights", true, false), ("black", true, true) };
            var _looks = new (string name, float yaw, float pitch)[] { ("rest", 0f, _rest), ("left", -_yawClamp, _rest * 0.5f), ("right", _yawClamp, _rest * 0.5f), ("up", 0f, _up), ("down", 0f, _down) };
            foreach (var _variant in _variants)
            {
                _surroundings.Enabled = _variant.black;
                _surroundings.Apply();
                _catLights.Enabled = _variant.catLights;
                yield return new WaitForSecondsRealtime(0.6f); // the cat lights' fade
                foreach (var _look in _looks)
                {
                    _vote.voteTimer = Mathf.Max(_vote.voteTimer, 60f);
                    SetEmbodiedLook(_look.yaw, _look.pitch);
                    yield return WaitStillCamera();
                    yield return new WaitForSecondsRealtime(0.2f);
                    yield return SaveClean($"pov-{_variant.name}-{_look.name}");
                }
                _vote.voteTimer = Mathf.Max(_vote.voteTimer, 60f);
                Journal.Record("pov.sweep", _variant.name);
                yield return SweepHead(-_yawClamp, _yawClamp, _rest * 0.5f, _rest * 0.5f, options.povSweepSeconds);
                yield return SweepHead(_yawClamp, 0f, _rest * 0.5f, _up, options.povSweepSeconds * 0.5f);
                // Let the recording end first: a capture taken while it still grabs frames comes out fully black.
                yield return new WaitForSeconds(2f);
            }
            yield return FovVariants(_vote, _camera, _yawClamp);
            if (_standIns != null)
            {
                Destroy(_standIns);
            }
            _surroundings.Enabled = _black;
            _surroundings.Apply();
            _catLights.Enabled = _catLit;
            SetEmbodiedLook(0f, _rest);
            Journal.Record("pov.tour", string.Format(CultureInfo.InvariantCulture, "yawClamp={0} rest={1} up={2} avatars={3}",
                _yawClamp, _rest, _up, FindFirstObjectByType<AvatarManager>()?.AvatarCount));
            inputBusy = false;
        }

        // seated-view botseats=1: a still copy of a real remote cat (seated pose, no script, no network) with a copy of the
        // cat light on every simulated player's seat, so a big table's day circle can be judged on captures.
        private GameObject SpawnStandInCats(CatSpotlights _catLights)
        {
            AvatarManager _avatars = FindFirstObjectByType<AvatarManager>();
            PlayerAvatar _model = null;
            foreach (PlayerAvatar _avatar in _avatars.GetAvatars())
            {
                if (_avatar != null && !_avatar.IsOwner)
                {
                    _model = _avatar;
                    break;
                }
            }
            CharacterManager _characters = FindFirstObjectByType<CharacterManager>();
            if (_model == null || _characters == null)
            {
                Journal.Record("pov.error", $"stand-ins: model={_model != null} characters={_characters != null}");
                return null;
            }
            Light _template = GetField<Light>(_catLights, "_template");
            string _eyePath = _model.EyePivot != null ? PathUnder(_model.transform, _model.EyePivot) : null;
            var _root = new GameObject("AutoplayStandIns");
            var _hold = new GameObject("hold");
            _hold.SetActive(false);
            int _count = 0;
            foreach (Character _character in _characters.GetCharacters(false))
            {
                if (_character == null || _character.isFake || _character.ownerClientId.Value < 100)
                {
                    continue;
                }
                GameObject _copy = Instantiate(_model.gameObject, _hold.transform);
                // Scripts first (some require others), the network object and the animator last: the bones keep the
                // seated pose they had when copied.
                StripScripts(_copy);
                foreach (Animator _animator in _copy.GetComponentsInChildren<Animator>(true))
                {
                    DestroyImmediate(_animator);
                }
                foreach (Unity.Netcode.NetworkObject _net in _copy.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true))
                {
                    DestroyImmediate(_net);
                }
                foreach (Collider _collider in _copy.GetComponentsInChildren<Collider>(true))
                {
                    _collider.enabled = false;
                }
                SeatPose _pose = _avatars.GetSeatPose(_character.ownerClientId.Value);
                _copy.transform.SetParent(_root.transform, false);
                _copy.transform.SetPositionAndRotation(_pose.Position, _pose.Rotation);
                _copy.transform.localScale = _model.transform.localScale;
                _copy.SetActive(true);
                if (_template != null)
                {
                    Transform _eye = _eyePath != null ? _copy.transform.Find(_eyePath) : null;
                    Vector3 _head = _eye != null ? _eye.position : _copy.transform.position;
                    Pose _light = CatSpotlightPose.Compute(_head + Vector3.down * GetField<float>(_catLights, "_aimBelowEye"),
                        _copy.transform.forward, GetField<float>(_catLights, "_elevation"), GetField<float>(_catLights, "_distance"));
                    Light _lamp = Instantiate(_template, _root.transform);
                    _lamp.transform.SetPositionAndRotation(_light.position, _light.rotation);
                    _lamp.enabled = true;
                }
                _count++;
            }
            Destroy(_hold);
            Journal.Record("pov.standins", $"count={_count} model={_model.ownerClientId.Value}");
            return _root;
        }

        // Destroys every script, those another one requires ([RequireComponent]) only once that one is gone.
        private static void StripScripts(GameObject _copy)
        {
            for (int _pass = 0; _pass < 10; _pass++)
            {
                MonoBehaviour[] _scripts = _copy.GetComponentsInChildren<MonoBehaviour>(true);
                if (_scripts.Length == 0)
                {
                    return;
                }
                foreach (MonoBehaviour _script in _scripts)
                {
                    if (_script != null && !Required(_script))
                    {
                        DestroyImmediate(_script);
                    }
                }
            }
        }

        private static bool Required(Component _target)
        {
            foreach (Component _other in _target.GetComponents<Component>())
            {
                if (_other == null || _other == _target)
                {
                    continue;
                }
                foreach (object _attribute in _other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    var _require = (RequireComponent)_attribute;
                    System.Type _type = _target.GetType();
                    if ((_require.m_Type0 != null && _require.m_Type0.IsAssignableFrom(_type))
                        || (_require.m_Type1 != null && _require.m_Type1.IsAssignableFrom(_type))
                        || (_require.m_Type2 != null && _require.m_Type2.IsAssignableFrom(_type)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static string PathUnder(Transform _root, Transform _child)
        {
            string _path = _child.name;
            for (Transform _t = _child.parent; _t != null && _t != _root; _t = _t.parent)
            {
                _path = _t.name + "/" + _path;
            }
            return _path;
        }

        // -autoplay-pov-fovs: the same looks at other vertical fields, each with the day's rest pitch for that field.
        private IEnumerator FovVariants(VoteState _vote, AvatarEmbodiedCamera _camera, float _yawClamp)
        {
            if (string.IsNullOrEmpty(options.povFovs))
            {
                yield break;
            }
            AvatarManager _avatars = FindFirstObjectByType<AvatarManager>();
            float _fov0 = _camera.FieldOfView;
            foreach (string _item in options.povFovs.Split(','))
            {
                if (!float.TryParse(_item.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float _fov))
                {
                    Journal.Record("pov.error", $"pov-fovs: bad value '{_item}'");
                    continue;
                }
                _camera.FieldOfView = _fov;
                float _rest = _avatars != null && _avatars.DayRingActive ? _avatars.DayRestPitchFor(_fov) : _camera.RestPitch;
                var _looks = new (string name, float yaw, float pitch)[] { ("rest", 0f, _rest), ("left", -_yawClamp, _rest * 0.5f), ("right", _yawClamp, _rest * 0.5f) };
                foreach (var _look in _looks)
                {
                    _vote.voteTimer = Mathf.Max(_vote.voteTimer, 60f);
                    SetEmbodiedLook(_look.yaw, _look.pitch);
                    yield return WaitStillCamera();
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return SaveClean($"pov-fov{_item.Trim()}-{_look.name}");
                }
                Journal.Record("pov.fov", string.Format(CultureInfo.InvariantCulture, "fov={0} rest={1:F1}", _fov, _rest));
            }
            _camera.FieldOfView = _fov0;
        }

        // Game time (Time.deltaTime): a recording advances the game a fixed step per captured frame, so the sweep stays
        // smooth in the video whatever the capture costs.
        private IEnumerator SweepHead(float _fromYaw, float _toYaw, float _fromPitch, float _toPitch, float _seconds)
        {
            for (float _t = 0f; _t < _seconds; _t += Time.deltaTime)
            {
                float _k = Mathf.SmoothStep(0f, 1f, _t / _seconds);
                SetEmbodiedLook(Mathf.Lerp(_fromYaw, _toYaw, _k), Mathf.Lerp(_fromPitch, _toPitch, _k));
                yield return null;
            }
            SetEmbodiedLook(_toYaw, _toPitch);
        }
    }
}
#endif
