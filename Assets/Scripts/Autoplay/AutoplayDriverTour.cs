#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using Avatars;
using ChatSystem;
using Cysharp.Threading.Tasks;
using GameLogic;
using GameLogic.GameStates;
using Smartphone;
using TMPro;
using TooltipSystem;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Autoplay
{
    /// <summary>
    /// Real-input "tour" (<c>-autoplay-real-input-tour</c>): once per game on each process (at night, while none of its
    /// seats is awake), the seat with a screen goes
    /// through the in-game interfaces a player uses besides powers and votes, by mouse and keyboard only: a tooltip
    /// (hover), the pause menu (button, audio slider, close), the tablet (Tab, an arrow swipe, the chat app: channel tab
    /// and input field, Tab again), and at the vote recap the emote wheel (hold T, turn, release). Each step journals
    /// <c>tour.&lt;step&gt; ok|miss</c> next to the usual <c>input.click</c> / <c>input.miss</c>.
    /// Not covered (known gaps): typing / sending in the chat (TMP_InputField reads IMGUI events, send needs a legacy
    /// Return), the notes ribbon (not placed in any scene), nested tooltip links (legacy Input.mousePosition).
    /// </summary>
    public sealed partial class AutoplayDriver
    {
        private bool tourDone;
        private bool emoteTourDone;

        private void UpdateTour(GameState _state)
        {
            if (!options.realInputTour || options.realInput == null || inputBusy)
            {
                return;
            }

            // Night, while none of this process's seats is awake (a fake-role layer on the host, a sleeping seat on a
            // client): free cursor, board view, no full-screen overlay (the recap's journal covers the HUD by design).
            if (!tourDone && _state is AwakeningState && Time.time - stateEnterGameTime > options.thinkDelay && !AnyControlledAwake())
            {
                tourDone = true;
                inputBusy = true;
                RunTour().Forget();
            }
            else if (!emoteTourDone && _state is VoteRecapState && CursorLocked && Time.time - stateEnterGameTime > options.thinkDelay)
            {
                emoteTourDone = true;
                inputBusy = true;
                RunEmoteTour().Forget();
            }
        }

        // A seat of this process woke up: its turn comes first (the tour holds the pointer meanwhile).
        private bool TourInterrupted()
        {
            if (!AnyControlledAwake())
            {
                return false;
            }
            TourStep("aborted", false, "reason=seat-awake");
            return true;
        }

        private bool AnyControlledAwake() => characterManager.GetCharacters(false)
            .Any(_c => _c && !_c.isFake && controlledIds.Contains(_c.ownerClientId.Value) && _c.isAwakened.Value);

        private void TourStep(string _step, bool _ok, string _detail = "")
            => Journal.Record($"tour.{_step}", $"{(_ok ? "ok" : "miss")}{(string.IsNullOrEmpty(_detail) ? "" : " " + _detail)}");

        private async UniTaskVoid RunTour()
        {
            try
            {
                // The host shows its own seat (no possessed bot) for the tour.
                await PrepareSeat(LocalSeat);
                await TourTooltip();
                if (TourInterrupted())
                {
                    return;
                }
                await TourPause();
                if (TourInterrupted())
                {
                    return;
                }
                await TourTablet();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception _exception)
            {
                Journal.Record("tour.error", $"{_exception.GetType().Name}: {_exception.Message}");
                Debug.LogException(_exception);
            }
            finally
            {
                inputBusy = false;
            }
        }

        private async UniTask TourTooltip()
        {
            // Wait (bounded) for something with a tooltip to be on screen and reachable (power bar, role cards…).
            HoverTooltipComponent _trigger = null;
            float _waitUntil = Time.realtimeSinceStartup + 6f;
            while (_trigger == null && Time.realtimeSinceStartup < _waitUntil)
            {
                _trigger = FindObjectsByType<HoverTooltipComponent>(FindObjectsSortMode.None)
                    .Where(_t => _t.isActiveAndEnabled)
                    .FirstOrDefault(_t => AutoplayUiLocator.Locate(_t.gameObject) is var _p && _p.found && string.IsNullOrEmpty(_p.reason));
                if (_trigger == null)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(0.25f), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                }
            }
            if (_trigger == null || TooltipManager.instance == null)
            {
                // Evidence: which triggers exist and why none is reachable (hidden HUD, off-screen, covered…).
                string _seen = string.Join(", ", FindObjectsByType<HoverTooltipComponent>(FindObjectsSortMode.None)
                    .Take(6)
                    .Select(_t => $"{AutoplayUiLocator.PathOf(_t.gameObject)}:{(_t.isActiveAndEnabled ? AutoplayUiLocator.Locate(_t.gameObject).reason ?? "ok" : "inactive")}"));
                TourStep("tooltip", false, $"reason=no-visible-trigger manager={(TooltipManager.instance != null)} " +
                                           $"locked={CursorLocked} triggers=[{_seen}]");
                return;
            }

            bool _hovered = await HoverTarget(_trigger.gameObject, "tooltip", 0f);
            // The host's power bar is rebuilt at every possession switch (the bar shows the possessed seat's powers):
            // a trigger can vanish under the pointer. Try fresh ones, bounded.
            for (int _retry = 0; _trigger == null && _retry < 5; _retry++)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(0.3f), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                _trigger = FindObjectsByType<HoverTooltipComponent>(FindObjectsSortMode.None)
                    .Where(_t => _t.isActiveAndEnabled)
                    .FirstOrDefault(_t => AutoplayUiLocator.Locate(_t.gameObject) is var _p && _p.found && string.IsNullOrEmpty(_p.reason));
                _hovered = _trigger != null && await HoverTarget(_trigger.gameObject, "tooltip", 0f);
            }
            if (_trigger == null)
            {
                TourStep("tooltip", false, "reason=target-destroyed");
                return;
            }
            float _deadline = Time.realtimeSinceStartup + options.clickEffectTimeout;
            while (_hovered && !TooltipManager.instance.IsTooltipOpenForGameObject(_trigger.gameObject) && Time.realtimeSinceStartup < _deadline)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
            }
            bool _open = _hovered && TooltipManager.instance.IsTooltipOpenForGameObject(_trigger.gameObject);
            TourStep("tooltip", _open, $"target={AutoplayUiLocator.PathOf(_trigger.gameObject)}");
            if (_open)
            {
                capture.Request("tour-tooltip", 0f);
            }
            // Leave it: back to a corner, so the tooltip closes before the next step.
            await VirtualInput.MoveTo(new Vector2(4f, Screen.height - 4f), options.pointerMoveSeconds).WithCancellation(Cancel);
        }

        private async UniTask TourPause()
        {
            GameObject _pauseButton = GameObject.Find("PauseButton");
            PanelComponent _panel = FindObjectsByType<PanelComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(_p => _p.name == "PausePanel");
            if (_panel == null)
            {
                TourStep("pause", false, "reason=no-pause-panel");
                return;
            }

            bool _opened = await ClickTarget(_pauseButton, "pause-open", () => _panel.isPanelOpen);
            TourStep("pause-open", _opened);
            if (!_opened)
            {
                return;
            }
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(0.4f), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                capture.Request("tour-pause", 0f);
                if (options.tourAudioSlider)
                {
                    await TourSlider(_panel);
                }

                Transform _exit = _panel.transform.Find("ExitButton");
                bool _closed = await ClickTarget(_exit ? _exit.gameObject : null, "pause-close", () => !_panel.isPanelOpen);
                TourStep("pause-close", _closed);
            }
            finally
            {
                if (_panel && _panel.isPanelOpen)
                {
                    _panel.ClosePanel(); // never leave the pause menu over the game (later clicks would all miss)
                }
            }
        }

        // An audio slider moved by a click on its track; the user's own volume (PlayerPrefs) is always put back.
        private async UniTask TourSlider(PanelComponent _panel)
        {
            Slider _slider = _panel.GetComponentsInChildren<Slider>().FirstOrDefault(_s => _s.isActiveAndEnabled && _s.interactable);
            Canvas _canvas = _slider ? _slider.GetComponentInParent<Canvas>() : null;
            if (_slider == null || _canvas == null)
            {
                TourStep("pause-slider", false, "reason=no-slider");
                return;
            }

            float _before = _slider.value;
            try
            {
                var _rect = (RectTransform)_slider.transform;
                Vector3[] _corners = new Vector3[4];
                _rect.GetWorldCorners(_corners);
                Canvas _root = _canvas.rootCanvas;
                Camera _camera = _root.renderMode == RenderMode.ScreenSpaceOverlay ? null : _root.worldCamera;
                Vector2 _min = RectTransformUtility.WorldToScreenPoint(_camera, _corners[0]);
                Vector2 _max = RectTransformUtility.WorldToScreenPoint(_camera, _corners[2]);
                float _fraction = _slider.normalizedValue > 0.5f ? 0.3f : 0.7f;
                Vector2 _point = new(Mathf.Lerp(_min.x, _max.x, _fraction), (_min.y + _max.y) * 0.5f);
                bool _moved = await ClickAt(_slider.gameObject, _point, "pause-slider", () => !Mathf.Approximately(_slider.value, _before));
                TourStep("pause-slider", _moved, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "slider={0} {1:0.00}->{2:0.00}", AutoplayUiLocator.PathOf(_slider.gameObject), _before, _slider.value));
            }
            finally
            {
                if (_slider && !Mathf.Approximately(_slider.value, _before))
                {
                    _slider.value = _before;
                }
            }
        }

        private async UniTask TourTablet()
        {
            SmartphoneController _tablet = FindAnyObjectByType<SmartphoneController>();
            if (_tablet == null)
            {
                TourStep("tablet-open", false, "reason=no-tablet");
                return;
            }

            bool _opened = _tablet.IsOpen || await PressKeyFor(Key.Tab, "tablet-open", () => _tablet.IsOpen);
            TourStep("tablet-open", _opened);
            if (!_opened)
            {
                return;
            }
            try
            {
                await TourTabletApps(_tablet);
            }
            finally
            {
                if (_tablet && _tablet.IsOpen)
                {
                    _tablet.TryClosePanel(); // never leave the tablet up over the game
                }
            }
        }

        private async UniTask TourTabletApps(SmartphoneController _tablet)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(0.6f), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
            capture.Request("tour-tablet", 0f);

            SmartphoneApp _first = _tablet.currentApp;
            bool _swiped = await PressKeyFor(Key.RightArrow, "tablet-swipe", () => _tablet.currentApp != _first) ||
                           await PressKeyFor(Key.LeftArrow, "tablet-swipe", () => _tablet.currentApp != _first);
            TourStep("tablet-swipe", _swiped, $"{_first?.name} -> {_tablet.currentApp?.name}");

            // Reach the chat app (bounded swipes), then its channel tab and input field.
            for (int _i = 0; _i < 4 && _tablet.currentApp != null && _tablet.currentApp.GetComponentInChildren<ChatPanel>(true) == null; _i++)
            {
                SmartphoneApp _was = _tablet.currentApp;
                await PressKeyFor(Key.RightArrow, "tablet-swipe", () => _tablet.currentApp != _was);
            }
            ChatPanel _chat = _tablet.currentApp != null ? _tablet.currentApp.GetComponentInChildren<ChatPanel>(true) : null;
            if (_chat == null)
            {
                TourStep("chat", false, "reason=chat-app-not-reached");
            }
            else
            {
                await UniTask.Delay(TimeSpan.FromSeconds(0.6f), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                await TourChat(_chat);
            }

            bool _closed = await PressKeyFor(Key.Tab, "tablet-close", () => !_tablet.IsOpen);
            TourStep("tablet-close", _closed);
        }

        private async UniTask TourChat(ChatPanel _chat)
        {
            ChatManager _manager = ChatManager.instance;
            if (_manager != null)
            {
                int _active = _manager.activeChatId;
                string[] _otherNames = _manager.discoveredChatIds.Where(_id => _id != _active)
                    .Select(_id => _manager.GetChatWindow(_id)?.chatName.ToString()).Where(_n => !string.IsNullOrEmpty(_n)).ToArray();
                CustomButton _tab = _chat.GetComponentsInChildren<CustomButton>()
                    .FirstOrDefault(_b => _b.GetComponentInChildren<TMP_Text>() is TMP_Text _label && _otherNames.Contains(_label.text));
                if (_tab != null)
                {
                    bool _switched = await ClickTarget(_tab.gameObject, "chat-tab", () => _manager.activeChatId != _active);
                    TourStep("chat-tab", _switched, $"{_active} -> {_manager.activeChatId}");
                }
                else
                {
                    TourStep("chat-tab", false, $"reason=single-channel discovered={_manager.discoveredChatIds.Count}");
                }
            }

            TMP_InputField _field = _chat.GetComponentInChildren<TMP_InputField>();
            try
            {
                bool _focused = await ClickTarget(_field ? _field.gameObject : null, "chat-field", () => _field.isFocused);
                TourStep("chat-field", _focused);
                if (_focused)
                {
                    capture.Request("tour-chat", 0f);
                }
            }
            finally
            {
                if (_field && _field.isFocused)
                {
                    _field.DeactivateInputField(); // typing is a known gap (IMGUI keystrokes): leave the field
                }
            }
        }

        // Emote wheel: hold T (first-person live), turn the virtual stick with mouse deltas, release on a slot.
        private async UniTaskVoid RunEmoteTour()
        {
            try
            {
                EmoteWheelInput _wheel = FindAnyObjectByType<EmoteWheelInput>();
                EmotePlaybackController _playback = _wheel ? _wheel.GetComponent<EmotePlaybackController>() : null;
                if (_wheel == null || _playback == null)
                {
                    TourStep("emote", false, "reason=no-wheel");
                    return;
                }

                bool _started = false;
                Action _onStarted = () => _started = true;
                _playback.EmoteStarted += _onStarted;
                try
                {
                    await EmoteWheelGesture(_wheel);
                    float _until = Time.realtimeSinceStartup + options.clickEffectTimeout;
                    while (!_started && Time.realtimeSinceStartup < _until)
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
                    }
                }
                finally
                {
                    _playback.EmoteStarted -= _onStarted;
                }
                TourStep("emote", _started);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception _exception)
            {
                InputError("emote", _exception);
            }
            finally
            {
                inputBusy = false;
            }
        }

        // Hold T, turn the virtual stick with mouse deltas, release (T is released even when cancelled).
        private async UniTask EmoteWheelGesture(EmoteWheelInput _wheel)
        {
            try
            {
                await VirtualInput.KeyDown(Key.T).WithCancellation(Cancel);
                float _deadline = Time.realtimeSinceStartup + 1f;
                while (!_wheel.IsOpen && Time.realtimeSinceStartup < _deadline)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
                }
                TourStep("emote-open", _wheel.IsOpen);
                if (_wheel.IsOpen)
                {
                    if (Cursor.lockState == CursorLockMode.Locked)
                    {
                        for (int _i = 0; _i < 12; _i++)
                        {
                            await VirtualInput.Look(new Vector2(25f, 0f)).WithCancellation(Cancel); // locked: delta stick
                        }
                    }
                    else
                    {
                        // Cursor not locked (the window guard frees it while the player window has the focus): the
                        // wheel reads the pointer's direction from the screen centre.
                        await VirtualInput.MoveTo(ScreenCentre + new Vector2(Screen.height * 0.25f, 0f), options.pointerMoveSeconds)
                            .WithCancellation(Cancel);
                    }
                    capture.Request("tour-emote-wheel", 0f);
                    await UniTask.Delay(TimeSpan.FromSeconds(0.2f), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                    // What the wheel will read on release (evidence when no emote plays).
                    object _view = _wheel.GetType().GetField("wheel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(_wheel);
                    object _count = _view?.GetType().GetProperty("Count")?.GetValue(_view);
                    Journal.Record("tour.emote-state", $"emotes={_count ?? "?"} lock={Cursor.lockState} pointer={VirtualInput.Position.x:0},{VirtualInput.Position.y:0} " +
                                                       $"screen={Screen.width}x{Screen.height}");
                }
            }
            finally
            {
                VirtualInput.ReleaseKey(Key.T);
            }
        }

        // Press a key and wait for its effect: input.click with mode=key, or input.miss reason=no-effect.
        private async UniTask<bool> PressKeyFor(Key _key, string _action, Func<bool> _effect)
        {
            await VirtualInput.PressKey(_key).WithCancellation(Cancel);
            float _deadline = Time.realtimeSinceStartup + options.clickEffectTimeout;
            while (!_effect())
            {
                if (Time.realtimeSinceStartup > _deadline)
                {
                    Journal.Record("input.miss", $"{_action} key={_key} mode=key reason=no-effect");
                    return false;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
            }
            Journal.Record("input.click", $"{_action} key={_key} mode=key");
            return true;
        }

        // Click a given point of a target (slider track): the real raycast must reach the target there.
        private async UniTask<bool> ClickAt(GameObject _target, Vector2 _point, string _action, Func<bool> _effect)
        {
            await VirtualInput.MoveTo(_point, options.pointerMoveSeconds).WithCancellation(Cancel);
            GameObject _hit = AutoplayUiLocator.TopHit(_point);
            string _where = string.Format(System.Globalization.CultureInfo.InvariantCulture, "target={0} pos={1:0},{2:0} hit={3} mode=pointer",
                AutoplayUiLocator.PathOf(_target), _point.x, _point.y, AutoplayUiLocator.PathOf(_hit));
            if (_hit == null || !(_hit == _target || _hit.transform.IsChildOf(_target.transform)))
            {
                Journal.Record("input.miss", $"{_action} {_where} reason=occluded");
                return false;
            }

            await VirtualInput.Click().WithCancellation(Cancel);
            Journal.Record("input.click", $"{_action} {_where}");
            float _deadline = Time.realtimeSinceStartup + options.clickEffectTimeout;
            while (!_effect())
            {
                if (Time.realtimeSinceStartup > _deadline)
                {
                    Journal.Record("input.miss", $"{_action} {_where} reason=no-effect");
                    return false;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
            }
            return true;
        }

        /// <summary>
        /// Real-input leave (lever quit-at with real input): pause button, then the pause menu's Leave button, as a
        /// player leaves. True once the client is disconnected; false = the caller shuts down directly.
        /// </summary>
        public async UniTask<bool> LeaveThroughPauseMenu()
        {
            await AcquireInput();
            try
            {
                GameObject _pauseButton = GameObject.Find("PauseButton");
                PanelComponent _panel = FindObjectsByType<PanelComponent>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(_p => _p.name == "PausePanel");
                if (_panel == null || !await ClickTarget(_pauseButton, "leave-pause", () => _panel.isPanelOpen))
                {
                    return false;
                }
                await UniTask.Delay(TimeSpan.FromSeconds(0.4f), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                Transform _leave = _panel.transform.Find("LeaveGameButton");
                return await ClickTarget(_leave ? _leave.gameObject : null, "leave-button",
                    () => networkManager == null || !networkManager.IsConnectedClient);
            }
            finally
            {
                inputBusy = false;
            }
        }
    }
}
#endif
