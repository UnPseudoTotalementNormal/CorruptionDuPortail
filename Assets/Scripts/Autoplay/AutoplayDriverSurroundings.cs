#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Globalization;
using Avatars;
using Board.BoardCameraSystem;
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
    /// the head with the mouse), room shown then pitch black, each followed by a slow head sweep left → right → up
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

        private IEnumerator PovTour()
        {
            yield return new WaitForSecondsRealtime(4f); // cards shown, panels out
            VoteState _vote = CurrentState as VoteState;
            BoardCameraManager _views = BoardCameraManager.instance;
            SeatedSurroundings _surroundings = FindFirstObjectByType<SeatedSurroundings>();
            AvatarEmbodiedCamera _camera = FindFirstObjectByType<AvatarEmbodiedCamera>();
            if (_vote == null || _views == null || _surroundings == null || _camera == null)
            {
                inputBusy = false;
                Journal.Record("pov.error", $"vote={_vote != null} views={_views != null} surroundings={_surroundings != null} camera={_camera != null}");
                yield break;
            }
            _views.SetCurrentBoardCamera(BoardCameraIdEnum.SeatedFirstPerson);
            yield return new WaitForSecondsRealtime(1.5f);
            bool _black = _surroundings.Enabled;
            float _yawClamp = GetField<float>(_camera, "_yawClamp");
            float _rest = _camera.RestPitch;
            float _up = -Mathf.Min(_camera.PitchClamp, 25f);
            var _variants = new (string name, bool black)[] { ("room", false), ("black", true) };
            var _looks = new (string name, float yaw, float pitch)[] { ("rest", 0f, _rest), ("left", -_yawClamp, _rest * 0.5f), ("right", _yawClamp, _rest * 0.5f), ("up", 0f, _up) };
            foreach (var _variant in _variants)
            {
                _surroundings.Enabled = _variant.black;
                _surroundings.Apply();
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
            _surroundings.Enabled = _black;
            _surroundings.Apply();
            SetEmbodiedLook(0f, _rest);
            Journal.Record("pov.tour", string.Format(CultureInfo.InvariantCulture, "yawClamp={0} rest={1} up={2} avatars={3}",
                _yawClamp, _rest, _up, FindFirstObjectByType<AvatarManager>()?.AvatarCount));
            inputBusy = false;
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
