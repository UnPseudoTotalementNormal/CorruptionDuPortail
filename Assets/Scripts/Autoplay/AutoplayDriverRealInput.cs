#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Threading;
using Board;
using Board.UI;
using Board.UI.PowerBar;
using Characters;
using Characters.Powers;
using Cysharp.Threading.Tasks;
using GameLogic;
using GameLogic.GameStates;
using UI;
using UI.BoardUI;
using UnityEngine;
using UnityEngine.EventSystems;
using Unpseudo.Autoplay;

namespace Autoplay
{
    /// <summary>
    /// Real-input mode (<c>-autoplay-real-input</c>): each action moves the virtual mouse to the real element and
    /// clicks, through the real EventSystem / raycasters. With a free cursor the pointer glides to the target; with a
    /// locked cursor (seated first-person, reticle armed) the bot turns the camera with mouse deltas until the target
    /// is under the reticle, as a player does. Each click must produce its game effect: <c>input.click</c>, else
    /// <c>input.miss</c> (what was hit instead) and the direct call, so the game goes on.
    /// </summary>
    public sealed partial class AutoplayDriver
    {
        // One UI action at a time on this process: the host possesses the acting seat, the pointer is shared. Flows
        // started from Update check and set it synchronously; flows started elsewhere wait for it (AcquireInput).
        private bool inputBusy;

        // Cancelled by End() (end of run, quit-at) as well as on destroy: no click may outlive the session.
        private CancellationTokenSource inputCancel;

        private CancellationToken Cancel => inputCancel != null ? inputCancel.Token : this.GetCancellationTokenOnDestroy();

        // The virtual devices: game actions with -autoplay-real-input, or lobby / tablet clicks with -autoplay-lobby-ui alone.
        private AutoplayVirtualInput VirtualInput => options.realInput ?? options.lobbyInput;

        private void BeginInput() => inputCancel = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());

        private void EndInput()
        {
            inputCancel?.Cancel();
            inputCancel?.Dispose();
            inputCancel = null;
            VirtualInput?.ReleaseAll();
            inputBusy = false;
        }

        private async UniTask AcquireInput()
        {
            while (inputBusy)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
            }
            inputBusy = true;
        }

        private void InputError(string _action, Exception _exception)
        {
            Journal.Record("input.error", $"{_action} {_exception.GetType().Name}: {_exception.Message}");
            Debug.LogException(_exception);
        }

        // Host: show the acting seat's own UI (power bar, buttons), rebuilt on the next frame.
        private async UniTask PrepareSeat(ulong _id)
        {
            if (options.possessActor)
            {
                Possess(_id);
            }
            await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
            await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
        }

        private async UniTaskVoid SleepByClick(Character _character)
        {
            inputBusy = true;
            ulong _id = _character.ownerClientId.Value;
            bool _clicked = false;
            try
            {
                await PrepareSeat(_id);
                SkipButton _button = FindAnyObjectByType<SkipButton>(FindObjectsInactive.Include);
                _clicked = await ClickTarget(_button ? _button.gameObject : null, $"sleep {_id}",
                    () => !_character || !_character.isAwakened.Value);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception _exception)
            {
                InputError($"sleep {_id}", _exception);
            }
            finally
            {
                inputBusy = false;
            }

            Journal.Record("sleep", $"{_id} via={(_clicked ? "click" : "direct")}");
            if (!_clicked && _character && _character.isAwakened.Value)
            {
                _character.SleepCharacterServerRpc();
            }
        }

        // The power's 3D object in the bar (collider live only while CanUse) is clicked; PowerUsageManager then starts
        // and drives the power exactly as for a player.
        private async UniTaskVoid PowerByClick(Character _character, BotTurn _turn, Power _power)
        {
            ulong _id = _character.ownerClientId.Value;
            bool _started = false;
            Action _onStart = () => _started = true;
            _power.onStartUse += _onStart;
            bool _clicked = false;
            try
            {
                await PrepareSeat(_id);
                // The bar rebuilds its objects when the powers change (a copy granted, a use spent): wait (bounded)
                // for this power's object, as a player waits for the bar to show it.
                PowersBarObject _object = null;
                float _barDeadline = Time.realtimeSinceStartup + 2f;
                while (_object == null && Time.realtimeSinceStartup < _barDeadline)
                {
                    PowersBar _bar = FindAnyObjectByType<PowersBar>();
                    _object = _bar ? _bar.GetPowerBarObject(_power) : null;
                    if (_object == null)
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(0.1f), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                    }
                }
                if (_object == null && !(CurrentState is AwakeningState && _character && _character.isAwakened.Value))
                {
                    // The night ended meanwhile (game over, the seat slept): the bar is gone, nothing for a player to click.
                    Journal.Record("input.skip", $"power {_id} {_power.powerName} reason=turn-over state={CurrentState?.GetType().Name}");
                    return;
                }
                _clicked = await ClickTarget(_object ? _object.gameObject : null, $"power {_id} {_power.powerName}",
                    () => _started || _power.isCurrentlyUsed);
            }
            catch (OperationCanceledException)
            {
                _power.onStartUse -= _onStart;
                return;
            }
            catch (Exception _exception)
            {
                InputError($"power {_id} {_power.powerName}", _exception);
            }
            finally
            {
                _power.onStartUse -= _onStart;
                _turn.activeSince = Time.time;
                _turn.idleSince = Time.time;
                _turn.clicking = false;
                inputBusy = false;
            }

            // Direct fallback only while this turn is still the seat's live turn (it may have slept / the phase moved on).
            bool _turnLive = CurrentState is AwakeningState && _character && _character.isAwakened.Value &&
                             turns.TryGetValue(_id, out BotTurn _current) && _current == _turn;
            if (!_clicked && !_started && !_power.isCurrentlyUsed && _turnLive)
            {
                _turn.direct = true;
                Journal.Record("power.direct", $"{_id} {_power.powerName}");
                try
                {
                    _power.StartUse();
                }
                catch (Exception _exception)
                {
                    Journal.Record("power.error", $"{_id} {_power.powerName} {_exception.GetType().Name}: {_exception.Message}");
                    Debug.LogException(_exception);
                    _turn.active = null;
                }
            }
            else if (!_clicked && !_started && !_power.isCurrentlyUsed)
            {
                _turn.active = null; // stale turn: nothing to drive
            }
        }

        // Picker card: hover (the card lifts, captured), then click. The pick took when the card left the pickable set
        // (picker closed, or reopened on the next step).
        private async UniTask PickCardByInput(CardPickerManager _picker, Card _card, int _opening)
        {
            await AcquireInput();
            string _via = "none";
            string _described = DescribeCard(_card);
            try
            {
                if (!_picker.IsPickerActive || !_card)
                {
                    Journal.Record("picker.click", $"#{_opening} {_described} via=none reason=picker-closed");
                    return;
                }
                bool _hovered = await HoverTarget(_card.gameObject, $"picker #{_opening}", options.pickerHoverDwell);
                if (_hovered)
                {
                    Journal.Record("picker.hover", $"#{_opening} {_described}");
                    capture.Request($"picker{_opening:000}-hover-{_described}", 0f);
                }

                bool _clicked = _card && _picker.IsPickerActive && await ClickTarget(_card.gameObject, $"picker #{_opening} {_described}",
                    () => !_picker.IsPickerActive || !_picker.PickableCards.Contains(_card));
                _via = _clicked ? "click" : "none";
                if (!_clicked && _card && _picker.IsPickerActive && _picker.PickableCards.Contains(_card))
                {
                    var _pointer = new PointerEventData(EventSystem.current);
                    _card.OnPointerClick(_pointer);
                    _card.OnPointerExit(_pointer);
                    _via = "direct";
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception _exception)
            {
                InputError($"picker #{_opening}", _exception);
            }
            finally
            {
                inputBusy = false;
            }
            Journal.Record("picker.click", $"#{_opening} {_described} via={_via}");
        }

        // Vote: hover the target's card (its vote panel slides out in 0.5 s), then click its vote button.
        private async UniTaskVoid VoteByInput(GameManager _gameManager, Character _voter, ulong _targetId)
        {
            ulong _id = _voter.ownerClientId.Value;
            bool _clicked = false;
            try
            {
                await PrepareSeat(_id);
                // At vote start the board cards are shown again with an animation: wait for the target's card.
                float _cardDeadline = Time.realtimeSinceStartup + 3f;
                while (BoardCardOf(_targetId) == null && Time.realtimeSinceStartup < _cardDeadline)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
                }
                Card _card = BoardCardOf(_targetId);
                var _panel = _card ? _card.GetComponentInChildren<Board.UI.VoteCanvas.VoteCanvas>(true) : null;
                if (_panel != null && await HoverTarget(_card.gameObject, $"vote {_id} -> {_targetId}", 0.5f))
                {
                    _clicked = await ClickTarget(_panel.voteButton ? _panel.voteButton.gameObject : null, $"vote {_id} -> {_targetId}",
                        () => HasVoted(_id));
                }
                else if (_panel == null)
                {
                    Journal.Record("input.miss", $"vote {_id} -> {_targetId} target=card:{_targetId} reason=not-found");
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception _exception)
            {
                InputError($"vote {_id} -> {_targetId}", _exception);
            }
            finally
            {
                inputBusy = false;
            }

            if (_clicked)
            {
                Journal.Record("vote", $"{_id} -> {_targetId} via=click");
            }
            else if (HasVoted(_id))
            {
                Journal.Record("vote", $"{_id} -> {_targetId} via=click-late"); // the click landed after the effect timeout
            }
            else if (CurrentState is VoteState)
            {
                VoteDirect(_gameManager, _id, _targetId);
            }
            else
            {
                Journal.Record("vote.late", $"{_id} -> {_targetId}"); // the vote closed while clicking
            }
        }

        // The vote screen's "Skip" button (a vote for SKIP_VOTE_ID; the direct path abstains instead).
        private async UniTaskVoid SkipVoteByClick(Character _voter)
        {
            ulong _id = _voter.ownerClientId.Value;
            bool _clicked = false;
            try
            {
                await PrepareSeat(_id);
                VoteStateUI _ui = FindAnyObjectByType<VoteStateUI>();
                CustomButton _button = _ui ? _ui.GetComponentsInChildren<CustomButton>(true)
                    .FirstOrDefault(_b => _b.name.IndexOf("Skip", StringComparison.OrdinalIgnoreCase) >= 0) : null;
                _clicked = await ClickTarget(_button ? _button.gameObject : null, $"vote-skip {_id}",
                    () => HasVoted(_id, VoteState.SKIP_VOTE_ID));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception _exception)
            {
                InputError($"vote-skip {_id}", _exception);
            }
            finally
            {
                inputBusy = false;
            }
            Journal.Record("vote.skip.click", $"{_id} via={(_clicked ? "click" : "none")}");
        }

        // The Mage clicks a character's card on the board; the role half goes through the (real-input) picker.
        private async UniTaskVoid PortalByClick(GameManager _gameManager, ulong _mageId, ulong _targetId)
        {
            bool _clicked = false;
            try
            {
                await PrepareSeat(_mageId);
                CardPickerManager _picker = CardPickerManager.instance;
                Card _card = BoardCardOf(_targetId);
                _clicked = await ClickTarget(_card ? _card.gameObject : null, $"portal {_mageId} -> {_targetId}",
                    () => (_picker != null && _picker.IsPickerActive) || !(CurrentState is TakeDownThePortalState));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception _exception)
            {
                InputError($"portal {_mageId} -> {_targetId}", _exception);
            }
            finally
            {
                inputBusy = false;
            }

            if (_clicked)
            {
                Journal.Record("portal.click", $"{_mageId} -> {_targetId} via=click");
            }
            else if (CurrentState is TakeDownThePortalState)
            {
                PortalDirect(_gameManager, _mageId, _targetId);
            }
        }

        private static Card BoardCardOf(ulong _ownerId)
        {
            BoardManager _board = BoardManager.instance;
            return _board == null ? null : _board.visibleCards.FirstOrDefault(_c => _c && _c.characterInfo && _c.characterInfo.ownerClientId.Value == _ownerId);
        }

        private bool HasVoted(ulong _voterId, ulong? _forId = null)
            => CurrentState is VoteState _vote && _vote.votesForPlayer.Any(_kv =>
                (!_forId.HasValue || _kv.Key == _forId.Value) && _kv.Value.Contains(_voterId));

        // The game's intent, not Cursor.lockState: the autoplay window guard unlocks the OS cursor whenever the player
        // window has the focus (to keep the user's mouse free), which used to send the seated first-person vote down
        // the free-pointer path (every pointer move then turned the head and the target left the screen).
        private static bool CursorLocked => Cursor.lockState == CursorLockMode.Locked ||
                                            (FindAnyObjectByType<Avatars.AvatarCameraArbiter>() is Avatars.AvatarCameraArbiter _arbiter && _arbiter.WantsLockedCursor);

        /// <summary>Brings the pointer (free cursor) or the reticle (locked cursor) onto the target and keeps it there
        /// <paramref name="_dwell"/> real seconds. False (journaled input.miss) when the target cannot be reached.</summary>
        private async UniTask<bool> HoverTarget(GameObject _target, string _action, float _dwell)
        {
            if (!CursorLocked)
            {
                await BringIntoView(_target, _action);
            }
            bool _reached = CursorLocked ? await AimAt(_target, _action) : await PointAt(_target, _action);
            if (_reached && _dwell > 0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_dwell), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
            }
            return _reached;
        }

        /// <summary>
        /// Brings the pointer / reticle onto <paramref name="_target"/>, clicks once, then waits for
        /// <paramref name="_effect"/>. Journals <c>input.click</c> or <c>input.miss</c> (with what was hit instead);
        /// false = the caller falls back to the direct path. Never clicks twice (a late effect must not be toggled back),
        /// and does not click at all when the effect already holds (<c>input.skip</c>).
        /// </summary>
        private async UniTask<bool> ClickTarget(GameObject _target, string _action, Func<bool> _effect)
        {
            if (_effect())
            {
                Journal.Record("input.skip", $"{_action} reason=already-done");
                return true;
            }

            if (!CursorLocked)
            {
                await BringIntoView(_target, _action);
            }
            bool _locked = CursorLocked;
            bool _reached = _locked ? await AimAt(_target, _action, _journalMiss: false) : await PointAt(_target, _action);
            if (!_reached && _locked && _action.StartsWith("vote", StringComparison.Ordinal))
            {
                // Out of the seated head's reach (or covered from this seat): switch view with the arrows, as a
                // player can, then point and click on the board overview (free cursor there).
                if (await SwitchViewToShow(_target, _action))
                {
                    _locked = CursorLocked;
                    _reached = _locked ? await AimAt(_target, _action) : await PointAt(_target, _action);
                }
                else
                {
                    await AimAt(_target, _action); // journals the miss with its reason
                }
            }
            if (!_reached)
            {
                return false;
            }
            if (CursorLocked != _locked)
            {
                // The cursor lock flipped meanwhile (the user focused the window, a panel opened): the aim is stale.
                Journal.Record("input.miss", $"{_action} target={AutoplayUiLocator.PathOf(_target)} reason=lock-changed");
                return false;
            }

            if (_locked && !AutoplayUiLocator.Reaches(AutoplayUiLocator.TopHit(ScreenCentre), _target))
            {
                // The target moved off the reticle while the hover settled (a panel sliding): aim again, once, as a
                // player would before clicking.
                Journal.Record("input.reaim", $"{_action} target={AutoplayUiLocator.PathOf(_target)} hit={AutoplayUiLocator.PathOf(AutoplayUiLocator.TopHit(ScreenCentre))}");
                if (!await AimAt(_target, _action))
                {
                    return false;
                }
            }

            Vector2 _at = _locked ? ScreenCentre : VirtualInput.Position;
            if (_locked && VirtualInput.Position != ScreenCentre)
            {
                await VirtualInput.WarpTo(ScreenCentre).WithCancellation(Cancel); // a locked cursor clicks at the centre
            }
            GameObject _hit = AutoplayUiLocator.TopHit(_at);
            await VirtualInput.Click().WithCancellation(Cancel);
            string _where = string.Format(System.Globalization.CultureInfo.InvariantCulture, "target={0} pos={1:0},{2:0} hit={3} mode={4}",
                AutoplayUiLocator.PathOf(_target), _at.x, _at.y, AutoplayUiLocator.PathOf(_hit), _locked ? "reticle" : "pointer");
            Journal.Record("input.click", $"{_action} {_where}");
            return await WaitForEffect(_action, _where, _effect);
        }

        private async UniTask<bool> WaitForEffect(string _action, string _where, Func<bool> _effect)
        {
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

        private static Vector2 ScreenCentre => new(Screen.width * 0.5f, Screen.height * 0.5f);

        private static readonly UnityEngine.InputSystem.Key[] ViewKeys =
        {
            UnityEngine.InputSystem.Key.DownArrow, UnityEngine.InputSystem.Key.UpArrow,
            UnityEngine.InputSystem.Key.LeftArrow, UnityEngine.InputSystem.Key.RightArrow,
        };

        /// <summary>
        /// The seated first-person view (the vote) turns with the mouse while the cursor stays free: a target out of
        /// view is brought into view as a player does, by looking around (mouse deltas, head clamped by the seat),
        /// else by switching view with the arrow keys (first person and the board overviews are neighbours), then the
        /// pointer clicks it. Journals <c>input.look</c> / <c>input.view</c>; nothing when the target is already shown.
        /// </summary>
        // Really on screen and reachable by the real raycast (TryAimPoint also answers for a target out of view: its
        // projected centre, to turn towards it).
        private static bool IsShown(GameObject _target)
        {
            AutoplayUiLocator.Probe _probe = AutoplayUiLocator.Locate(_target);
            return _probe.found && string.IsNullOrEmpty(_probe.reason);
        }

        private async UniTask BringIntoView(GameObject _target, string _action)
        {
            if (_target == null || IsShown(_target))
            {
                return;
            }
            Avatars.AvatarEmbodiedCamera _firstPerson = FindAnyObjectByType<Avatars.AvatarEmbodiedCamera>();
            if (_firstPerson != null && _firstPerson.IsActive &&
                await AimAt(_target, _action, _journalMiss: false) && IsShown(_target))
            {
                Journal.Record("input.look", $"{_action} target={AutoplayUiLocator.PathOf(_target)}");
                return;
            }

            if (_action.StartsWith("vote", StringComparison.Ordinal))
            {
                await SwitchViewToShow(_target, _action); // never during a picker: the arrows rebuild the role shelf
            }
        }

        // Arrow keys move between the seated first-person view and the board overviews: try each until the target
        // is shown and reachable. Journals input.view on success.
        private async UniTask<bool> SwitchViewToShow(GameObject _target, string _action)
        {
            Board.BoardCameraSystem.BoardCameraManager _views = FindAnyObjectByType<Board.BoardCameraSystem.BoardCameraManager>();
            foreach (UnityEngine.InputSystem.Key _key in ViewKeys)
            {
                string _from = _views != null ? _views.CurrentBoardCameraId.ToString() : "?";
                await VirtualInput.PressKey(_key).WithCancellation(Cancel);
                await UniTask.Delay(TimeSpan.FromSeconds(options.viewBlendSeconds), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                await WaitForStillCamera(Camera.main);
                if (_target != null && IsShown(_target))
                {
                    Journal.Record("input.view", $"{_action} key={_key} {_from}->{(_views != null ? _views.CurrentBoardCameraId.ToString() : "?")} " +
                                                 $"target={AutoplayUiLocator.PathOf(_target)}");
                    return true;
                }
                AutoplayUiLocator.Probe _probe = _target != null ? AutoplayUiLocator.Locate(_target) : default;
                Journal.Record("input.view-try", $"{_action} key={_key} {_from}->{(_views != null ? _views.CurrentBoardCameraId.ToString() : "?")} " +
                                                 $"found={_probe.found} reason={_probe.reason ?? "-"} hit={AutoplayUiLocator.PathOf(_probe.hit)}");
            }
            return false;
        }

        /// <summary>
        /// Clicks a UI Toolkit element with the pointer: a screen-space panel (<paramref name="_raw"/> null, e.g. the
        /// role card overlay) or one drawn into a RenderTexture shown on <paramref name="_raw"/> (the tablet).
        /// <paramref name="_find"/> is re-run before and after the glide (panels rebuild their trees on any change).
        /// Journals input.click / input.miss like <see cref="ClickTarget"/>; false = the caller falls back.
        /// </summary>
        public async UniTask<bool> ClickUitk(Func<UnityEngine.UIElements.VisualElement> _find, string _action, Func<bool> _effect,
            UnityEngine.UI.RawImage _raw = null)
        {
            if (_effect())
            {
                Journal.Record("input.skip", $"{_action} reason=already-done");
                return true;
            }
            if (!await PointAtUitk(_find, _action, _raw))
            {
                return false;
            }
            if (!AutoplayUiLocator.TryLocateUitkElement(_find(), _raw, out Vector2 _screen, out string _detail))
            {
                Journal.Record("input.miss", $"{_action} target={AutoplayUiLocator.DescribeElement(_find())} {_detail} mode=pointer");
                return false;
            }
            await VirtualInput.MoveTo(_screen, 0f).WithCancellation(Cancel);
            await VirtualInput.Click().WithCancellation(Cancel);
            string _where = $"target={AutoplayUiLocator.DescribeElement(_find())} {_detail} mode=pointer";
            Journal.Record("input.click", $"{_action} {_where}");
            return await WaitForEffect(_action, _where, _effect);
        }

        /// <summary>Hovers a UI Toolkit element (see <see cref="ClickUitk"/>) for <paramref name="_dwell"/> real seconds.</summary>
        public async UniTask<bool> HoverUitk(Func<UnityEngine.UIElements.VisualElement> _find, string _action, float _dwell,
            UnityEngine.UI.RawImage _raw = null)
        {
            if (!await PointAtUitk(_find, _action, _raw))
            {
                return false;
            }
            await UniTask.Delay(TimeSpan.FromSeconds(_dwell), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
            return true;
        }

        // Glide to a UI Toolkit element; a freshly rebuilt element gets a few frames to be laid out.
        private async UniTask<bool> PointAtUitk(Func<UnityEngine.UIElements.VisualElement> _find, string _action, UnityEngine.UI.RawImage _raw)
        {
            Vector2 _screen = default;
            string _detail = null;
            bool _located = false;
            // Not laid out yet, or covered for a moment (an overlay fading out): wait like a player, bounded. A panel
            // still rebuilding (the lobby tablet during a join's first sync) gets longer: it settles, a player waits.
            float _deadline = Time.realtimeSinceStartup + Mathf.Max(1f, options.occludedWaitSeconds);
            float _layoutDeadline = Time.realtimeSinceStartup + 5f;
            while (!_located && (Time.realtimeSinceStartup < _deadline ||
                                 (_detail == "reason=not-laid-out" && Time.realtimeSinceStartup < _layoutDeadline)))
            {
                _located = AutoplayUiLocator.TryLocateUitkElement(_find(), _raw, out _screen, out _detail);
                if (!_located)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
                }
            }
            if (!_located)
            {
                Journal.Record("input.miss", $"{_action} target={AutoplayUiLocator.DescribeElement(_find())} {_detail} mode=pointer");
                return false;
            }
            await VirtualInput.MoveTo(_screen, options.pointerMoveSeconds).WithCancellation(Cancel);
            return true;
        }

        // Free cursor: glide to the first point where the real raycast reaches the target.
        private async UniTask<bool> PointAt(GameObject _target, string _action)
        {
            AutoplayUiLocator.Probe _probe = AutoplayUiLocator.Locate(_target);
            // Covered for a moment (a picker card still settling, a panel sliding): wait like a player, bounded.
            float _waitUntil = Time.realtimeSinceStartup + options.occludedWaitSeconds;
            while (_probe.found && _probe.reason == "occluded" && Time.realtimeSinceStartup < _waitUntil)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
                _probe = AutoplayUiLocator.Locate(_target);
            }
            if (!_probe.found || !string.IsNullOrEmpty(_probe.reason))
            {
                Journal.Record("input.miss", $"{_action} {_probe.Describe(_target)}");
                return false;
            }

            await VirtualInput.MoveTo(_probe.screen, options.pointerMoveSeconds).WithCancellation(Cancel);
            // The target may have moved while gliding (tweens): re-check where the click will land.
            _probe = AutoplayUiLocator.Locate(_target);
            if (!_probe.found || !string.IsNullOrEmpty(_probe.reason))
            {
                Journal.Record("input.miss", $"{_action} {_probe.Describe(_target)}");
                return false;
            }
            if (Vector2.Distance(_probe.screen, VirtualInput.Position) > 2f)
            {
                await VirtualInput.MoveTo(_probe.screen, 0f).WithCancellation(Cancel);
            }
            return true;
        }

        // Locked cursor: turn the seated camera with mouse deltas until the target sits under the reticle. Steered in
        // angles, not pixels: the error is the angle between the camera axis and the ray to the target, the gain is the
        // camera's measured rotation per delta unit (a hovered card moves by itself, which would fool a pixel ratio).
        // The camera follows with damping, so each correction is one step then a wait until the camera stops turning.
        // The camera is left where it ends (a player does not look back either).
        private async UniTask<bool> AimAt(GameObject _target, string _action, bool _journalMiss = true)
        {
            Vector2 _centre = ScreenCentre;
            Camera _camera = Camera.main;
            AutoplayUiLocator.TryAimPoint(_target, out Vector2 _start, out _);
            float _degreesPerUnit = 0f;
            string _reason = "out-of-reach";
            int _steps = 0;
            bool _aimed = false;
            if (_camera == null)
            {
                _reason = "no-camera";
            }
            else if (AutoplayUiLocator.IsScreenFixed(_target) && !AutoplayUiLocator.Reaches(AutoplayUiLocator.TopHit(_centre), _target))
            {
                // A screen-space element does not move with the camera: the reticle can only reach it if it already
                // sits at the screen centre. A seated player cannot click it either (finding, not a bot limit).
                _reason = "screen-fixed";
            }
            else
            {
                await WaitForStillCamera(_camera); // a phase transition may still be moving it
                for (; _steps < options.aimMaxSteps; _steps++)
                {
                    if (_camera == null)
                    {
                        _reason = "no-camera";
                        break;
                    }
                    if (AutoplayUiLocator.Reaches(AutoplayUiLocator.TopHit(_centre), _target))
                    {
                        _aimed = true;
                        break;
                    }
                    if (!AutoplayUiLocator.TryAimPoint(_target, out Vector2 _point, out string _why))
                    {
                        _reason = _why;
                        break;
                    }

                    // Angles to turn: right (+) and up (+). Mouse right turns right, mouse up looks up.
                    Vector3 _local = Quaternion.Inverse(_camera.transform.rotation) * _camera.ScreenPointToRay(_point).direction;
                    Vector2 _error = new(Mathf.Atan2(_local.x, _local.z) * Mathf.Rad2Deg,
                        Mathf.Atan2(_local.y, new Vector2(_local.x, _local.z).magnitude) * Mathf.Rad2Deg);
                    if (_error.sqrMagnitude < 1e-6f)
                    {
                        _reason = "occluded"; // dead centre but the raycast does not reach it: something covers it
                        break;
                    }
                    Vector2 _delta = _degreesPerUnit > 0f ? _error * (0.9f / _degreesPerUnit) : _error.normalized * 20f; // first step measures
                    _delta = Vector2.ClampMagnitude(_delta, 600f);

                    Quaternion _before = _camera.transform.rotation;
                    await VirtualInput.Look(_delta).WithCancellation(Cancel);
                    await WaitForStillCamera(_camera);
                    if (_camera == null)
                    {
                        _reason = "no-camera";
                        break;
                    }
                    float _turned = Quaternion.Angle(_before, _camera.transform.rotation);
                    float _expected = _degreesPerUnit > 0f ? _delta.magnitude * _degreesPerUnit : 1f;
                    if (_turned < 0.02f && _expected > 0.2f)
                    {
                        _reason = "look-clamped"; // the delta no longer turns the camera: seat look bounds reached
                        break;
                    }
                    if (_turned >= 0.02f)
                    {
                        float _ratio = _turned / _delta.magnitude;
                        _degreesPerUnit = _degreesPerUnit <= 0f ? _ratio : Mathf.Lerp(_degreesPerUnit, _ratio, 0.5f);
                    }
                }
            }

            if (_aimed)
            {
                if (VirtualInput.Position != _centre)
                {
                    await VirtualInput.WarpTo(_centre).WithCancellation(Cancel); // hover / click where the reticle is
                }
                await UniTask.Delay(TimeSpan.FromSeconds(options.aimSettleSeconds), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                return true;
            }

            if (!_journalMiss)
            {
                return false;
            }
            AutoplayUiLocator.TryAimPoint(_target, out Vector2 _last, out _);
            Journal.Record("input.miss", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} target={1} aim={2:0},{3:0} start={4:0},{5:0} steps={6} degPerUnit={7:0.000} hit={8} mode=reticle reason={9}", _action,
                AutoplayUiLocator.PathOf(_target), _last.x, _last.y, _start.x, _start.y, _steps, _degreesPerUnit,
                AutoplayUiLocator.PathOf(AutoplayUiLocator.TopHit(_centre)), _reason));
            return false;
        }

        // Frames until the (damped) camera stops turning, bounded.
        private async UniTask WaitForStillCamera(Camera _camera)
        {
            if (_camera == null)
            {
                return;
            }
            Quaternion _previous = _camera.transform.rotation;
            for (int _frame = 0; _frame < options.aimSettleFrames; _frame++)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
                if (_camera == null)
                {
                    return;
                }
                Quaternion _now = _camera.transform.rotation;
                if (_frame >= 2 && Quaternion.Angle(_previous, _now) < 0.01f)
                {
                    return;
                }
                _previous = _now;
            }
        }
    }
}
#endif
