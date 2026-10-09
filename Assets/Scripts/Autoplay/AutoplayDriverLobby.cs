#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameLogic;
using Smartphone;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Button = UnityEngine.UIElements.Button;
using Unpseudo.Autoplay;
using UnityEngine.UIElements;

namespace Autoplay
{
    /// <summary>
    /// Real-input lobby (<c>-autoplay-lobby-ui</c>, implied by <c>-autoplay-real-input</c>): the composition is set on the
    /// lobby tablet with the mouse (UI Toolkit drawn into a RenderTexture: "★ Preset classique", then Max "+" and
    /// Imposé "+" per forced role), each seat with a screen clicks "Prêt", and the game starts by itself
    /// (<c>LobbyState.TryAutoStart</c>). Journals <c>lobby.preset</c>, <c>lobby.force</c>, <c>lobby.ready</c> with
    /// <c>via=click|direct</c> next to <c>input.click</c> / <c>input.miss</c>.
    /// </summary>
    public sealed partial class AutoplayDriver
    {
        private bool lobbyTabletSettled;

        private const string LobbyDocumentName = "LobbyRolesUITK";
        private const string LobbyRawImageName = "LobbyRolesRawImage";

        private VisualElement LobbyRoot()
        {
            GameObject _go = GameObject.Find(LobbyDocumentName);
            UIDocument _document = _go ? _go.GetComponent<UIDocument>() : null;
            return _document != null ? _document.rootVisualElement?.Q<VisualElement>("lobby-roles") : null;
        }

        private static RawImage LobbyRawImage()
            => FindObjectsByType<RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(_r => _r.name == LobbyRawImageName);

        /// <summary>The lobby tablet is up on the lobby app (it rises by itself in the lobby; Tab otherwise).</summary>
        private async UniTask<bool> EnsureLobbyTablet()
        {
            SmartphoneController _tablet = FindAnyObjectByType<SmartphoneController>();
            float _deadline = Time.realtimeSinceStartup + 10f;
            bool _tabPressed = false;
            while (Time.realtimeSinceStartup < _deadline)
            {
                RawImage _raw = LobbyRawImage();
                if (_tablet != null && _tablet.IsOpen && _raw != null && _raw.isActiveAndEnabled && LobbyRoot() != null)
                {
                    if (!lobbyTabletSettled)
                    {
                        // First time: let the tablet finish rising before aiming at it.
                        await UniTask.Delay(TimeSpan.FromSeconds(0.6f), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                        lobbyTabletSettled = true;
                    }
                    return true;
                }
                if (_tablet != null && !_tablet.IsOpen && !_tabPressed && Time.realtimeSinceStartup > _deadline - 7f)
                {
                    _tabPressed = true;
                    await PressKeyFor(Key.Tab, "lobby-tablet", () => _tablet.IsOpen);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
            }
            Journal.Record("input.miss", $"lobby-tablet reason=not-open open={(_tablet != null && _tablet.IsOpen)}");
            return false;
        }

        /// <summary>Clicks a lobby-tablet element found by <paramref name="_find"/> (re-queried: the UI rebuilds on
        /// every change), scrolling its list with the wheel when it is out of view.</summary>
        private async UniTask<bool> ClickLobbyElement(Func<VisualElement, VisualElement> _find, string _action, Func<bool> _effect)
        {
            if (!await EnsureLobbyTablet())
            {
                return false;
            }
            RawImage _raw = LobbyRawImage();
            await ScrollIntoView(_find, _raw);
            return await ClickUitk(() => LobbyRoot() is VisualElement _root ? _find(_root) : null, _action, _effect, _raw);
        }

        // The element's ScrollView: wheel over it until the element sits inside the viewport (bounded). Re-queried at
        // every step: the tablet rebuilds its tree on any change (a preset click recreates the ScrollView).
        private async UniTask ScrollIntoView(Func<VisualElement, VisualElement> _find, RawImage _raw)
        {
            for (int _notch = 0; _notch < 40; _notch++)
            {
                VisualElement _root = LobbyRoot();
                VisualElement _element = _root != null ? _find(_root) : null;
                ScrollView _scroll = _element?.GetFirstAncestorOfType<ScrollView>();
                if (_scroll == null)
                {
                    return;
                }
                Rect _view = _scroll.contentViewport.worldBound;
                if (float.IsNaN(_view.height) || _view.height <= 0f)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, Cancel); // freshly rebuilt: not laid out yet
                    continue;
                }
                Rect _bound = _element.worldBound;
                if (float.IsNaN(_bound.yMin) || float.IsNaN(_bound.yMax))
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
                    continue;
                }
                // In view = its centre inside the viewport (an element taller than the viewport counts as in view).
                if (_bound.center.y >= _view.yMin && _bound.center.y <= _view.yMax)
                {
                    return;
                }
                Vector2 _over = default;
                string _overDetail = null;
                bool _overFound = false;
                float _waitUntil = Time.realtimeSinceStartup + options.occludedWaitSeconds;
                while (!(_overFound = AutoplayUiLocator.TryLocateUitkElement(_scroll.contentViewport, _raw, out _over, out _overDetail)) &&
                       Time.realtimeSinceStartup < _waitUntil)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, Cancel); // covered for a moment (an overlay fading out)
                }
                if (!_overFound)
                {
                    Journal.Record("input.miss", $"lobby-scroll target={AutoplayUiLocator.DescribeElement(_scroll.contentViewport)} {_overDetail}");
                    return;
                }
                await VirtualInput.MoveTo(_over, _notch == 0 ? options.pointerMoveSeconds : 0f).WithCancellation(Cancel);
                // Panel y grows downwards: an element below the viewport needs a wheel-down (negative y).
                float _direction = _bound.yMax > _view.yMax ? -1f : 1f;
                float _before = _scroll.scrollOffset.y;
                await VirtualInput.Scroll(new Vector2(0f, options.wheelNotch * _direction)).WithCancellation(Cancel);
                Journal.Record("input.scroll", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} dir={1} offset={2:0}->{3:0} {4}", AutoplayUiLocator.DescribeElement(_element), _direction < 0 ? "down" : "up", _before, _scroll.scrollOffset.y, _overDetail));
                if (Mathf.Approximately(_before, _scroll.scrollOffset.y))
                {
                    Journal.Record("input.miss", $"lobby-scroll target={AutoplayUiLocator.DescribeElement(_element)} reason=scroll-stuck");
                    return; // the wheel does not move the list (end reached, or wheel not delivered)
                }
            }
        }

        /// <summary>Host: "★ Preset classique".</summary>
        public async UniTask<bool> LobbyApplyClassicPreset()
        {
            await AcquireInput();
            try
            {
                Func<bool> _active = () => LobbyRoot()?.Q<Button>(className: "lobby-roles__preset-classic")?.ClassListContains("lobby-roles__preset-classic--active") == true;
                if (_active())
                {
                    Journal.Record("lobby.preset", "classic via=already"); // clicking it again could toggle it
                    return true;
                }
                bool _clicked = await ClickLobbyElement(_root => _root.Q<Button>(className: "lobby-roles__preset-classic"), "lobby-preset", _active);
                Journal.Record("lobby.preset", $"classic via={(_clicked ? "click" : "direct")}");
                return _clicked;
            }
            finally
            {
                inputBusy = false;
            }
        }

        // The role's unit on the tablet: its card label contains the fragment.
        private static VisualElement LobbyUnit(VisualElement _root, string _fragment)
            => _root?.Query<VisualElement>(className: "lobby-roles__unit").ToList().FirstOrDefault(_unit =>
                _unit.Query<Label>().ToList().Any(_l => !string.IsNullOrEmpty(_l.text) &&
                                                         _l.text.IndexOf(_fragment, StringComparison.OrdinalIgnoreCase) >= 0));

        private static int StepperValue(VisualElement _unit, bool _forced)
        {
            VisualElement _ctl = _unit?.Query<VisualElement>(className: "lobby-roles__ctl").ToList()
                .FirstOrDefault(_c => _c.ClassListContains("lobby-roles__ctl--forced") == _forced);
            Label _value = _ctl?.Q<Label>(className: "lobby-roles__step-val");
            return _value != null && int.TryParse(_value.text, out int _v) ? _v : -1;
        }

        private static Button StepperPlus(VisualElement _unit, bool _forced)
        {
            VisualElement _ctl = _unit?.Query<VisualElement>(className: "lobby-roles__ctl").ToList()
                .FirstOrDefault(_c => _c.ClassListContains("lobby-roles__ctl--forced") == _forced);
            return _ctl?.Query<Button>(className: "lobby-roles__step-btn").ToList().ElementAtOrDefault(1);
        }

        /// <summary>Host: guarantee one role in the composition (Max "+" if needed, then Imposé "+").</summary>
        public async UniTask<bool> LobbyForceRole(string _fragment)
        {
            await AcquireInput();
            try
            {
                if (LobbyUnit(LobbyRoot(), _fragment) == null)
                {
                    Journal.Record("input.miss", $"lobby-force {_fragment} reason=not-found");
                    return false;
                }
                if (StepperValue(LobbyUnit(LobbyRoot(), _fragment), false) < 1)
                {
                    await ClickLobbyElement(_root => StepperPlus(LobbyUnit(_root, _fragment), false), $"lobby-max {_fragment}",
                        () => StepperValue(LobbyUnit(LobbyRoot(), _fragment), false) >= 1);
                }
                if (StepperValue(LobbyUnit(LobbyRoot(), _fragment), true) >= 1)
                {
                    Journal.Record("lobby.force", $"{_fragment} via=already");
                    return true; // the preset already imposes it
                }
                bool _clicked = await ClickLobbyElement(_root => StepperPlus(LobbyUnit(_root, _fragment), true), $"lobby-force {_fragment}",
                    () => StepperValue(LobbyUnit(LobbyRoot(), _fragment), true) >= 1);
                Journal.Record("lobby.force", $"{_fragment} via={(_clicked ? "click" : "direct")}");
                return _clicked;
            }
            finally
            {
                inputBusy = false;
            }
        }

        /// <summary>Every seat with a screen: "Prêt" (simulated bots are ready by themselves).</summary>
        public async UniTask<bool> LobbyReady()
        {
            await AcquireInput();
            try
            {
                ulong _self = networkManager.LocalClientId;
                var _holder = CompositionRoot.For(networkManager).LobbyPlayerInfoHolder;
                Func<bool> _ready = () => _holder != null && _holder.TryGetPlayerInfo(_self, out var _info) && _info.isReady;
                if (_ready())
                {
                    Journal.Record("lobby.ready", $"{_self} via=already");
                    return true;
                }
                // A player clicks once the tablet shows the button: wait (bounded) for its first layout pass, a
                // just-opened lobby panel is not laid out yet (input.miss reason=not-laid-out otherwise).
                float _layoutDeadline = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < _layoutDeadline)
                {
                    Button _start = LobbyRoot()?.Q<VisualElement>(className: "lobby-roles__footer")?.Q<Button>(className: "lobby-roles__start");
                    // Same test as AutoplayUiLocator: a laid-out world rectangle (its centre is NaN until the position
                    // is resolved, even when the width already is).
                    if (_start != null && _start.worldBound.width > 0f && !float.IsNaN(_start.worldBound.center.x)
                        && !float.IsNaN(_start.worldBound.center.y))
                    {
                        break;
                    }
                    await UniTask.Delay(TimeSpan.FromSeconds(0.1f), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                }
                bool _clicked = (await ClickLobbyElement(_root => _root.Q<VisualElement>(className: "lobby-roles__footer")?.Q<Button>(className: "lobby-roles__start"),
                                                 "lobby-ready", _ready));
                Journal.Record("lobby.ready", $"{_self} via={(_clicked ? "click" : "direct")}");
                if (!_clicked && _holder != null)
                {
                    _holder.RequestSetReady(true);
                }
                return _clicked;
            }
            finally
            {
                inputBusy = false;
            }
        }

        private static VisualElement RoleCardRoot() => AutoplayUiLocator.UitkElement("RoleCardUI", _r => _r.Q<VisualElement>("role-card"));

        private static bool RoleCardOpen() => RoleCardRoot() is VisualElement _card && _card.pickingMode == PickingMode.Position;

        /// <summary>
        /// Host, lobby: a role card face on the tablet (RenderTexture) opens the role detail overlay (screen-space UI
        /// Toolkit), whose close button closes it. Journals <c>lobby.role-card open|close ok|miss</c>.
        /// </summary>
        public async UniTask<bool> LobbyPeekRoleCard()
        {
            await AcquireInput();
            try
            {
                // The card face of the first role on the tablet (a RoleCardElement; its ClickEvent opens the detail).
                bool _opened = await ClickLobbyElement(_root => _root.Q<VisualElement>(className: "lobby-roles__unit")?.Q<UI.Cards.RoleCardElement>(),
                    "lobby-role-card", RoleCardOpen);
                Journal.Record("lobby.role-card", $"open {(_opened ? "ok" : "miss")}");
                if (!_opened)
                {
                    return false;
                }
                await UniTask.Delay(TimeSpan.FromSeconds(options.roleCardDwell), DelayType.Realtime, PlayerLoopTiming.Update, Cancel);
                capture.Request("lobby-role-card", 0f);
                bool _closed = await ClickUitk(() => RoleCardRoot()?.Q<Button>("close"), "role-card-close", () => !RoleCardOpen());
                // The card animates out and only stops taking clicks once collapsed (~0.4 s): wait for it.
                float _collapseDeadline = Time.realtimeSinceStartup + 2f;
                while (_closed && RoleCardRoot() is VisualElement _card && !_card.ClassListContains("cdp-is-collapsed") &&
                       Time.realtimeSinceStartup < _collapseDeadline)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, Cancel);
                }
                Journal.Record("lobby.role-card", $"close {(_closed ? "ok" : "miss")}");
                return _closed;
            }
            finally
            {
                inputBusy = false;
            }
        }

        /// <summary>The footer's status line (ready count or the composition error), for a start that never comes.</summary>
        public string LobbyStatus()
            => LobbyRoot()?.Q<Label>(className: "lobby-roles__reason")?.text ?? "none";

        /// <summary>Host, ending screen: "Terminer la partie" by real input (lever replay). False = no button or a miss:
        /// the caller then ends the game directly.</summary>
        public async UniTask<bool> ClickEndGame()
        {
            await AcquireInput();
            try
            {
                UI.ShutOffGameButton _button = FindAnyObjectByType<UI.ShutOffGameButton>();
                UI.CustomButton _custom = _button ? _button.GetComponent<UI.CustomButton>() : null;
                if (!_custom)
                {
                    Journal.Record("input.miss", "end-game target=ShutOffGameButton reason=not-found");
                    return false;
                }
                bool _clicked = false;
                void OnClicked() => _clicked = true;
                _custom.onButtonClicked += OnClicked;
                try
                {
                    return await ClickTarget(_button.gameObject, "end-game", () => _clicked);
                }
                finally
                {
                    if (_custom)
                    {
                        _custom.onButtonClicked -= OnClicked;
                    }
                }
            }
            finally
            {
                inputBusy = false;
            }
        }
    }
}
#endif
