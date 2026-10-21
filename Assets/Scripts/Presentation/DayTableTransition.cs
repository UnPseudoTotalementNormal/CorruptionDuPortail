using System;
using System.Collections.Generic;
using System.Threading;
using Avatars;
using Cysharp.Threading.Tasks;
using FX.Candle;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace Presentation
{
    /// <summary>
    /// T23, the day in first person (mockups jour-pov v1, choices in memory "T23"). When the day starts (the seated
    /// Embodied mode: the vote and its recap) the cards fly from the table to the screen (<see cref="DayScreenRows"/>),
    /// then the dark swallows the table (T3): under the black the table and the cork disappear, the vote candle goes
    /// down to the floor; when the cats' lights come back only the players are left, in a circle, and the view rises
    /// from the table to them (K2). When the day ends the dark brings the table back, then the cards fly back onto it.
    /// Local presentation only; reacts to the camera mode the arbiter broadcasts.
    /// </summary>
    public class DayTableTransition : MonoBehaviour
    {
        [SerializeField] private CameraModeChannel _cameraMode;
        [Tooltip("The game loop: the chaining that follows the vote is still day (the table stays away, Poyo 2026-10-21).")]
        [SerializeField] private GameLogic.GameManager _gameManager;
        [SerializeField] private DayScreenRows _rows;
        [SerializeField] private AvatarEmbodiedCamera _embodiedCamera;
        [SerializeField] private VoteCandle _voteCandle;
        [Tooltip("What disappears by day: the table, its board, the cork wall (and the round table trial option).")]
        [SerializeField] private List<GameObject> _tableParts = new();
        [Tooltip("World UI lying on the table that would float without it (the powers bar, the skip button).")]
        [SerializeField] private List<GameObject> _tableUi = new();
        [Tooltip("Mockups jour-pov v1 K2: the seated view's rest pitch once the table is gone (degrees, + = down).")]
        [SerializeField] private float _dayRestPitch = 20f;
        [Tooltip("Seconds for the view to rise to the cats.")]
        [SerializeField] private float _riseSeconds = 1.2f;
        [Tooltip("Seconds the cards fly before the dark comes.")]
        [SerializeField] private float _cardsFlightSeconds = 1.1f;
        [SerializeField] private float _toBlackSeconds = 0.35f;
        [SerializeField] private float _blackHoldSeconds = 0.25f;
        [SerializeField] private float _fromBlackSeconds = 0.6f;
        [Tooltip("Sorting order of the black overlay (above the game's HUD, under the pause menu).")]
        [SerializeField] private int _overlaySortingOrder = 40;

        private readonly Dictionary<Renderer, bool> _renderers = new();
        private readonly Dictionary<Collider, bool> _colliders = new();
        private readonly Dictionary<GameObject, bool> _uiActive = new();
        private Image _black;
        private bool _day;
        private bool _tableHidden;
        private float _nightRestPitch;
        private int _epoch;

        /// <summary>Whether the table is hidden right now (by day, once the dark has passed).</summary>
        public bool TableHidden => _tableHidden;

        private void Awake()
        {
            Assert.IsNotNull(_cameraMode, "DayTableTransition._cameraMode is not wired — wire the CameraModeChannel asset.");
            Assert.IsNotNull(_gameManager, "DayTableTransition._gameManager is not wired — wire the GameManager in GameScene.");
            Assert.IsNotNull(_rows, "DayTableTransition._rows is not wired — wire the DayScreenRows in GameScene.");
            Assert.IsNotNull(_embodiedCamera, "DayTableTransition._embodiedCamera is not wired — wire the AvatarEmbodiedCamera.");
            _nightRestPitch = _embodiedCamera != null ? _embodiedCamera.RestPitch : 35f;
            BuildOverlay();
        }

        // The day: the seated vote and its recap (Embodied), and the chaining that follows them (Poyo, 2026-10-21: the table
        // stays away during the chaining; the chaining sequence itself is left as it is). It ends when the night starts.
        private void Update()
        {
            bool _embodied = _cameraMode != null && _cameraMode.Current == CameraMode.Embodied;
            bool _chainingAfterVote = _day && _gameManager != null && _gameManager.IsSpawned
                && _gameManager.GetGameState(_gameManager.currentGameStateIndex.Value) is GameLogic.GameStates.ChainingState;
            OnDayChanged(_embodied || _chainingAfterVote);
        }

        private void OnDestroy()
        {
            _epoch++;
            ShowTable(true);
        }

        private void OnDayChanged(bool _isDay)
        {
            if (_isDay == _day)
            {
                return;
            }
            _day = _isDay;
            int _run = ++_epoch;
            CancellationToken _alive = this.GetCancellationTokenOnDestroy();
            if (_isDay)
            {
                StartDay(_run, _alive).Forget();
            }
            else
            {
                EndDay(_run, _alive).Forget();
            }
        }

        private async UniTaskVoid StartDay(int _run, CancellationToken _alive)
        {
            Diagnostics.Logs.GameLog.Info("ui", "day.start");
            _rows.Enter();
            if (!await Wait(_cardsFlightSeconds, _run, _alive)) return;
            if (!await Fade(1f, _toBlackSeconds, _run, _alive)) return;
            ShowTable(false);
            if (!await Wait(_blackHoldSeconds, _run, _alive)) return;
            if (!await Fade(0f, _fromBlackSeconds, _run, _alive)) return;
            _embodiedCamera.SetRestPitch(_dayRestPitch, _riseSeconds);
        }

        private async UniTaskVoid EndDay(int _run, CancellationToken _alive)
        {
            Diagnostics.Logs.GameLog.Info("ui", "day.end");
            // The cards head back to the table at once: the night may deal its own right away (the chaining, the
            // portal), and a card dealt meanwhile must land on the table, not on screen. They are still flying when
            // the light comes back on the table.
            _rows.Exit();
            if (_tableHidden)
            {
                if (!await Fade(1f, _toBlackSeconds, _run, _alive)) return;
                ShowTable(true);
                _embodiedCamera.SetRestPitch(_nightRestPitch, 0f);
                if (!await Wait(_blackHoldSeconds, _run, _alive)) return;
                if (!await Fade(0f, _fromBlackSeconds, _run, _alive)) return;
            }
            else
            {
                ShowTable(true);
                _embodiedCamera.SetRestPitch(_nightRestPitch, 0f);
                SetBlack(0f);
            }
        }

        // ---- the table

        private void ShowTable(bool _show)
        {
            if (_show == !_tableHidden)
            {
                return;
            }
            _tableHidden = !_show;
            if (!_show)
            {
                _renderers.Clear();
                _colliders.Clear();
                _uiActive.Clear();
                foreach (GameObject _part in _tableParts)
                {
                    if (_part == null)
                    {
                        continue;
                    }
                    foreach (Renderer _renderer in _part.GetComponentsInChildren<Renderer>(true))
                    {
                        _renderers[_renderer] = _renderer.enabled;
                        _renderer.enabled = false;
                    }
                    // The cork wall's collider stood between the eye and the cats in front.
                    foreach (Collider _collider in _part.GetComponentsInChildren<Collider>(true))
                    {
                        _colliders[_collider] = _collider.enabled;
                        _collider.enabled = false;
                    }
                }
                foreach (GameObject _ui in _tableUi)
                {
                    if (_ui != null)
                    {
                        _uiActive[_ui] = _ui.activeSelf;
                        _ui.SetActive(false);
                    }
                }
            }
            else
            {
                foreach (KeyValuePair<Renderer, bool> _entry in _renderers)
                {
                    if (_entry.Key != null) _entry.Key.enabled = _entry.Value;
                }
                foreach (KeyValuePair<Collider, bool> _entry in _colliders)
                {
                    if (_entry.Key != null) _entry.Key.enabled = _entry.Value;
                }
                foreach (KeyValuePair<GameObject, bool> _entry in _uiActive)
                {
                    if (_entry.Key != null) _entry.Key.SetActive(_entry.Value);
                }
                _renderers.Clear();
                _colliders.Clear();
                _uiActive.Clear();
            }
            if (_voteCandle != null)
            {
                _voteCandle.SetOnFloor(!_show);
            }
            Diagnostics.Logs.GameLog.Info("ui", "day.table", "shown", _show);
        }

        // ---- the black

        private void BuildOverlay()
        {
            var _canvasObject = new GameObject("DayTransitionBlack", typeof(RectTransform), typeof(Canvas));
            _canvasObject.transform.SetParent(transform, false);
            var _canvas = _canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = _overlaySortingOrder;
            var _imageObject = new GameObject("Black", typeof(RectTransform), typeof(Image));
            var _rect = (RectTransform)_imageObject.transform;
            _rect.SetParent(_canvasObject.transform, false);
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
            _black = _imageObject.GetComponent<Image>();
            _black.raycastTarget = false;
            SetBlack(0f);
        }

        private void SetBlack(float _alpha)
        {
            if (_black == null)
            {
                return;
            }
            _black.color = new Color(0f, 0f, 0f, _alpha);
            _black.enabled = _alpha > 0f;
        }

        private async UniTask<bool> Fade(float _to, float _seconds, int _run, CancellationToken _alive)
        {
            float _from = _black != null ? _black.color.a : 0f;
            float _time = 0f;
            while (_time < _seconds)
            {
                if (_run != _epoch) return false;
                _time += Time.unscaledDeltaTime;
                SetBlack(Mathf.Lerp(_from, _to, Mathf.SmoothStep(0f, 1f, _time / _seconds)));
                await UniTask.Yield(PlayerLoopTiming.Update, _alive);
            }
            if (_run != _epoch) return false;
            SetBlack(_to);
            return true;
        }

        private async UniTask<bool> Wait(float _seconds, int _run, CancellationToken _alive)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_seconds), DelayType.UnscaledDeltaTime, cancellationToken: _alive);
            return _run == _epoch;
        }
    }
}
