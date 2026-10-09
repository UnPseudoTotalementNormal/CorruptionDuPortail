#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using UI.RoleBook;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Unpseudo.Autoplay;

namespace Autoplay
{
    /// <summary>
    /// Real-input tour of the main menu's role book (<c>-autoplay-role-book</c>): the "Personnages" button opens it (cover
    /// swing), the dog-eared corner turns pages, the right arrow key too, each faction ribbon opens its chapter, the left
    /// corner turns back, the close tab closes it. Each step journals <c>book.&lt;step&gt; ok|miss</c> (with the page
    /// shown) next to <c>input.click</c> / <c>input.miss</c>, and captures the book. The UGS login screen covers the menu
    /// and the autoplay never signs in: the caller lifts it first, as a signed-in player's would be.
    /// </summary>
    public static class AutoplayRoleBookTour
    {
        private const float TurnPause = 1.1f;

        public static IEnumerator Run(AutoplayVirtualInput _input, AutoplayJournal _journal, AutoplayCapture _capture)
        {
            RoleBookController _book = UnityEngine.Object.FindAnyObjectByType<RoleBookController>();
            GameObject _button = GameObject.Find("MenuCanvas/RoleBookButton");
            if (_book == null || _button == null)
            {
                _journal.Record("book.open", $"miss reason={(_book == null ? "no-book" : "no-button")}");
                yield break;
            }

            bool _opened = false;
            yield return AutoplayMenuTour.Click(_input, _journal, "book-open", _button, () => _book.IsOpen, _ok => _opened = _ok);
            _journal.Record("book.open", _opened ? "ok" : "miss");
            if (!_opened)
            {
                yield break;
            }
            yield return Pause(1.4f); // cover swing + left page unfolding
            _capture.Request("book-open", 0f);
            // The menu's canvas draws over the book: its buttons must be put away while it is open.
            _journal.Record("book.menu-hidden", _button.activeInHierarchy ? "miss" : "ok");
            yield return Pause(0.6f);

            for (int _i = 0; _i < 2; _i++)
            {
                yield return Step(_input, _journal, _capture, _book, $"turn-corner-{_i + 1}", Find(_book, "corner-right"), +1);
            }

            string _before = Folio(_book);
            yield return _input.PressKey(Key.RightArrow);
            yield return Pause(TurnPause);
            _journal.Record("book.turn-key", $"{(Folio(_book) != _before ? "ok" : "miss")} page={Folio(_book)}");

            VisualElement _ribbons = Find(_book, "book-ribbons");
            for (int _r = 0; _ribbons != null && _r < _ribbons.childCount; _r++)
            {
                int _index = (_r + 1) % _ribbons.childCount; // élus, marginaux, then back to the anomalies
                yield return Step(_input, _journal, _capture, _book, $"ribbon-{_index}", _ribbons.ElementAt(_index), 0);
            }

            yield return Step(_input, _journal, _capture, _book, "turn-more", Find(_book, "corner-right"), +1);
            yield return Step(_input, _journal, _capture, _book, "turn-back", Find(_book, "corner-left"), -1);

            bool _closed = false;
            yield return ClickUitk(_input, _journal, "book-close", Find(_book, "book-close"), () => !_book.IsOpen, _ok => _closed = _ok);
            _journal.Record("book.close", _closed ? "ok" : "miss");
            yield return Pause(0.8f);
            // The menu hid its own buttons while the book was open (its canvas draws over the book): they must be back.
            _journal.Record("book.menu-back", _button != null && _button.activeInHierarchy ? "ok" : "miss");
        }

        // One click on the book; ok when the folio changed (a turn) or, for a ribbon (delta 0), when a page is shown.
        private static IEnumerator Step(AutoplayVirtualInput _input, AutoplayJournal _journal, AutoplayCapture _capture,
            RoleBookController _book, string _name, VisualElement _target, int _delta)
        {
            string _before = Folio(_book);
            bool _clicked = false;
            yield return ClickUitk(_input, _journal, "book-" + _name, _target, () => true, _ok => _clicked = _ok);
            yield return Pause(TurnPause);
            bool _ok = _clicked && (_delta == 0 || Folio(_book) != _before);
            _journal.Record("book." + _name, $"{(_ok ? "ok" : "miss")} page={Folio(_book)}");
            _capture.Request("book-" + _name, 0f);
        }

        private static IEnumerator ClickUitk(AutoplayVirtualInput _input, AutoplayJournal _journal, string _action, VisualElement _target,
            Func<bool> _effect, Action<bool> _result)
        {
            if (!AutoplayUiLocator.TryLocateUitkElement(_target, null, out Vector2 _screen, out string _detail))
            {
                _journal.Record("input.miss", $"{_action} target={AutoplayUiLocator.DescribeElement(_target)} {_detail} mode=pointer");
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

        private static VisualElement Find(RoleBookController _book, string _name) =>
            _book.GetComponent<UIDocument>()?.rootVisualElement?.Q<VisualElement>(_name);

        private static string Folio(RoleBookController _book) => (Find(_book, "page-right-number") as Label)?.text ?? "?";

        private static IEnumerator Pause(float _seconds)
        {
            yield return new WaitForSecondsRealtime(_seconds);
        }
    }
}
#endif
