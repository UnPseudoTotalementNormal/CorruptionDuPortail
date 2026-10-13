#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using Avatars.Cosmetics;
using CorruptionDuPortail.Domain;
using UI.CatCustomization;
using UnityEngine;
using UnityEngine.UIElements;
using Unpseudo.Autoplay;

namespace Autoplay
{
    /// <summary>
    /// Real-input tour of the main menu's cat customization (<c>-autoplay-cat-custo</c>): a click on the menu's cat opens
    /// the screen, each part's tab is opened and every vignette of it clicked (the cat changes), the cat is turned
    /// (scripted: the virtual mouse has no drag), then thrown (it keeps spinning, slows down, stops), « Menu » closes it and the menu's cat wears the new look. Each step
    /// journals <c>cat.&lt;step&gt; ok|miss</c> next to <c>input.click</c> / <c>input.miss</c> and captures the screen.
    /// The look in force at the start is put back at the end (the tour must not change a player's saved cat).
    /// The UGS login screen covers the menu: the caller lifts it first.
    /// </summary>
    public static class AutoplayCatCustomizationTour
    {
        private const float ChoicePause = 0.9f;

        // The demo's final cat, a mix of the four skins.
        private static readonly CatLook FinalLook = new(3, 1, 2, 2);

        public static IEnumerator Run(AutoplayVirtualInput _input, AutoplayJournal _journal, AutoplayCapture _capture)
        {
            CatCustomizationController _screen = UnityEngine.Object.FindAnyObjectByType<CatCustomizationController>();
            GameObject _menuCat = GameObject.Find("MenuCanvas/MenuCat");
            if (_screen == null || _menuCat == null)
            {
                _journal.Record("cat.open", $"miss reason={(_screen == null ? "no-screen" : "no-menu-cat")}");
                yield break;
            }
            CatLook _saved = CatLookStore.Load();
            CatLookStore.Save(CatLook.Default);
            yield return Pause(1.2f);
            _capture.Request("cat-menu", 0f);

            bool _opened = false;
            yield return AutoplayMenuTour.Click(_input, _journal, "cat-open", _menuCat, () => _screen.IsOpen, _ok => _opened = _ok);
            _journal.Record("cat.open", _opened ? "ok" : "miss");
            if (!_opened)
            {
                CatLookStore.Save(_saved);
                yield break;
            }
            yield return Pause(1.0f);
            _journal.Record("cat.menu-hidden", _menuCat.activeInHierarchy ? "miss" : "ok");
            _capture.Request("cat-open", 0f);

            VisualElement _tabs = Find(_screen, "cat-tabs");
            for (int _t = 0; _tabs != null && _t < _tabs.childCount; _t++)
            {
                CatSlot _slot = CatLook.Slots[_t];
                yield return ClickUitk(_input, _journal, $"cat-tab-{_slot}", _tabs.ElementAt(_t), () => true, _ => { });
                yield return Pause(0.6f);
                VisualElement _cells = Find(_screen, "cat-cells");
                for (int _c = 0; _cells != null && _c < _cells.childCount; _c++)
                {
                    CatLook _before = _screen.Stage.Look;
                    yield return ClickUitk(_input, _journal, $"cat-{_slot}-{_c}", _cells.ElementAt(_c), () => true, _ => { });
                    yield return Pause(ChoicePause);
                    CatLook _after = _screen.Stage.Look;
                    _journal.Record($"cat.{_slot}-{_c}", $"{(_c == 0 && _after == _before || _after != _before ? "ok" : "miss")} look={_after}");
                    _capture.Request($"cat-{_slot}-{_c}", 0f);
                    _cells = Find(_screen, "cat-cells"); // rebuilt on each choice
                }
                // Leave the part on the demo's final choice.
                yield return ClickUitk(_input, _journal, $"cat-{_slot}-final", CellOf(_screen, _slot, FinalLook.Get(_slot)), () => true, _ => { });
                yield return Pause(0.7f);
            }

            // Turn the cat around and back (what a drag on it does).
            for (int _f = 0; _f < 90; _f++)
            {
                _screen.Stage.AddYaw(_f < 45 ? 8f : -8f);
                yield return null;
            }
            _journal.Record("cat.turn", "ok scripted");
            // Thrown: it keeps spinning, slowing down, then stops.
            _screen.Throw(900f);
            yield return Pause(0.4f);
            bool _spinning = _screen.IsSpinning;
            yield return Pause(2.6f);
            _journal.Record("cat.throw", $"{(_spinning && !_screen.IsSpinning ? "ok" : "miss")} spinning={_spinning} stopped={!_screen.IsSpinning}");
            bool _saveOk = CatLookStore.Load() == FinalLook;
            _journal.Record("cat.saved", $"{(_saveOk ? "ok" : "miss")} look={CatLookStore.Load()}");
            _capture.Request("cat-final", 0f);
            yield return Pause(1.0f);

            bool _closed = false;
            yield return ClickUitk(_input, _journal, "cat-close", Find(_screen, "cat-back"), () => !_screen.IsOpen, _ok => _closed = _ok);
            _journal.Record("cat.close", _closed ? "ok" : "miss");
            yield return Pause(1.0f);
            _journal.Record("cat.menu-back", _menuCat != null && _menuCat.activeInHierarchy ? "ok" : "miss");
            _capture.Request("cat-menu-after", 0f);
            yield return Pause(2.0f);

            CatLookStore.Save(_saved);
        }

        /// <summary>
        /// <c>-autoplay-cat-anims</c>: the customization screen's cat plays every animation of the artist (the game's
        /// Cat_Avatar Animator, driven like the game drives it): the emotes 1-4 (coucou, crying with tears, accusing
        /// with the frown, fiery pose), chained then an emote tried while chained (struggle), the tablet. Journals
        /// <c>catanim.&lt;state&gt; ok|miss</c> (the Animator really reached the state) and captures each.
        /// </summary>
        public static IEnumerator RunAnimations(AutoplayVirtualInput _input, AutoplayJournal _journal, AutoplayCapture _capture)
        {
            CatCustomizationController _screen = UnityEngine.Object.FindAnyObjectByType<CatCustomizationController>();
            GameObject _menuCat = GameObject.Find("MenuCanvas/MenuCat");
            if (_screen == null || _menuCat == null)
            {
                _journal.Record("catanim.open", "miss reason=no-screen");
                yield break;
            }
            if (!_screen.IsOpen)
            {
                bool _opened = false;
                yield return AutoplayMenuTour.Click(_input, _journal, "catanim-open", _menuCat, () => _screen.IsOpen, _ok => _opened = _ok);
                if (!_opened)
                {
                    _journal.Record("catanim.open", "miss");
                    yield break;
                }
                yield return Pause(1f);
            }
            Animator _animator = _screen.Stage.Animator;
            if (_animator == null)
            {
                _journal.Record("catanim.open", "miss reason=no-animator");
                yield break;
            }
            _screen.Stage.ResetYaw();
            _journal.Record("catanim.idle", $"{(_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle") ? "ok" : "miss")}");
            yield return Pause(2f);

            var _emotes = new (int id, string state)[] { (1, "WaveLoop"), (2, "Crying"), (3, "Accused Idle"), (4, "Fiery Idle") };
            foreach ((int _id, string _state) in _emotes)
            {
                _animator.SetInteger("EmoteId", _id);
                _animator.SetBool("Emoting", true);
                yield return Pause(3.2f);
                _journal.Record($"catanim.{_state}", Reached(_animator, _state));
                _capture.Request($"catanim-{_state}", 0f);
                yield return Pause(0.8f);
                _animator.SetBool("Emoting", false);
                yield return Pause(1.2f);
            }

            _animator.SetBool("Chained", true);
            yield return Pause(4.5f);
            _journal.Record("catanim.Chained Idle", Reached(_animator, "Chained Idle"));
            _capture.Request("catanim-chained", 0f);
            _animator.SetInteger("EmoteId", 1);
            _animator.SetBool("Emoting", true); // an emote tried while chained
            yield return Pause(0.25f);
            _journal.Record("catanim.Chained Struggle", Reached(_animator, "Chained Struggle"));
            _capture.Request("catanim-struggle", 0f);
            yield return Pause(1.5f);
            _animator.SetBool("Emoting", false);
            yield return Pause(1f);
            _animator.SetBool("Chained", false);
            yield return Pause(1.5f);

            _animator.SetBool("Tablet", true);
            yield return Pause(2.5f);
            _journal.Record("catanim.Look at Tablet Idle", Reached(_animator, "Look at Tablet Idle"));
            _capture.Request("catanim-tablet", 0f);
            yield return Pause(1.5f);
            _animator.SetBool("Tablet", false);
            yield return Pause(2f);
            _journal.Record("catanim.back-to-idle", Reached(_animator, "Idle"));

            bool _closed = false;
            yield return ClickUitk(_input, _journal, "catanim-close", Find(_screen, "cat-back"), () => !_screen.IsOpen, _ok => _closed = _ok);
            yield return Pause(1f);
        }

        private static string Reached(Animator _animator, string _state) =>
            _animator.GetCurrentAnimatorStateInfo(0).IsName(_state) || _animator.GetNextAnimatorStateInfo(0).IsName(_state) ? "ok" : "miss";

        private static VisualElement CellOf(CatCustomizationController _screen, CatSlot _slot, byte _id)
        {
            VisualElement _cells = Find(_screen, "cat-cells");
            int _index = 0;
            foreach (CatCosmeticItem _item in _screen.Catalog.ItemsOf(_slot))
            {
                if (_item.id == _id) return _cells?.ElementAt(_index);
                _index++;
            }
            return null;
        }

        private static IEnumerator ClickUitk(AutoplayVirtualInput _input, AutoplayJournal _journal, string _action, VisualElement _target,
            Func<bool> _effect, Action<bool> _result)
        {
            if (_target == null || !AutoplayUiLocator.TryLocateUitkElement(_target, null, out Vector2 _screen, out string _detail))
            {
                _journal.Record("input.miss", $"{_action} target={(_target == null ? "null" : AutoplayUiLocator.DescribeElement(_target))} mode=pointer");
                _result(false);
                yield break;
            }
            yield return _input.MoveTo(_screen, 0.35f);
            yield return _input.Click();
            _journal.Record("input.click", $"{_action} target={AutoplayUiLocator.DescribeElement(_target)} {_detail} mode=pointer");
            float _deadline = Time.realtimeSinceStartup + 2f;
            while (!_effect())
            {
                if (Time.realtimeSinceStartup > _deadline)
                {
                    _journal.Record("input.miss", $"{_action} reason=no-effect");
                    _result(false);
                    yield break;
                }
                yield return null;
            }
            _result(true);
        }

        private static VisualElement Find(CatCustomizationController _screen, string _name) =>
            _screen.GetComponent<UIDocument>()?.rootVisualElement?.Q<VisualElement>(_name);

        private static IEnumerator Pause(float _seconds)
        {
            yield return new WaitForSecondsRealtime(_seconds);
        }
    }
}
#endif
