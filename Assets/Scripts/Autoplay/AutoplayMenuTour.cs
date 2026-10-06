#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Unpseudo.Autoplay;

namespace Autoplay
{
    /// <summary>
    /// Real-input main menu (<c>-autoplay-menu-ui</c>), outside UGS / Steam (Login, Host and Join are never confirmed):
    /// captures the screen as a player first sees it, then opens and closes the Host and Join panels and moves an audio
    /// slider with the mouse. Each step journals <c>menu.&lt;step&gt; ok|miss</c> next to <c>input.click</c> /
    /// <c>input.miss</c>. Also the rejected-join check: the disconnect notification must be the first thing under the
    /// pointer over its Dismiss button (above the login screen), then dismissing it must hide it.
    /// </summary>
    public static class AutoplayMenuTour
    {
        public static IEnumerator Run(AutoplayVirtualInput _input, AutoplayJournal _journal, AutoplayCapture _capture)
        {
            _capture.Request("menu-first-screen", 0.2f);
            yield return new WaitForSecondsRealtime(0.5f);

            yield return Panel(_input, _journal, "host", "MenuCanvas/HostButton", "HostPanel", "HostPanel/Panel/Close");
            yield return Panel(_input, _journal, "join", "MenuCanvas/JoinButton", "JoinPanel", "JoinPanel/Panel/Close");
            yield return Slider(_input, _journal);
            _capture.Request("menu-after-tour", 0.2f);
            yield return new WaitForSecondsRealtime(0.4f);
        }

        private static IEnumerator Panel(AutoplayVirtualInput _input, AutoplayJournal _journal, string _name, string _openPath,
            string _panelName, string _closePath)
        {
            CanvasGroup _group = UnityEngine.Object.FindObjectsByType<CanvasGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(_g => _g.name == _panelName);
            if (_group == null)
            {
                _journal.Record($"menu.{_name}-open", "miss reason=no-panel");
                yield break;
            }

            bool _opened = false;
            yield return Click(_input, _journal, $"menu-{_name}-open", GameObject.Find(_openPath), () => _group.blocksRaycasts && _group.alpha > 0.5f, _ok => _opened = _ok);
            _journal.Record($"menu.{_name}-open", _opened ? "ok" : "miss");
            if (!_opened)
            {
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.5f);

            bool _closed = false;
            yield return Click(_input, _journal, $"menu-{_name}-close", GameObject.Find(_closePath), () => !_group.blocksRaycasts, _ok => _closed = _ok);
            _journal.Record($"menu.{_name}-close", _closed ? "ok" : "miss");
            yield return new WaitForSecondsRealtime(0.4f);
        }

        private static IEnumerator Slider(AutoplayVirtualInput _input, AutoplayJournal _journal)
        {
            GameObject _audio = GameObject.Find("MenuCanvas/AudioPanel");
            Slider _slider = _audio != null ? _audio.GetComponentsInChildren<Slider>().FirstOrDefault(_s => _s.isActiveAndEnabled && _s.interactable) : null;
            Canvas _parentCanvas = _slider != null ? _slider.GetComponentInParent<Canvas>() : null;
            if (_slider == null || _parentCanvas == null)
            {
                _journal.Record("menu.slider", "miss reason=no-slider");
                yield break;
            }

            float _before = _slider.value;
            try
            {
                yield return MoveSlider(_input, _journal, _slider, _parentCanvas.rootCanvas, _before);
            }
            finally
            {
                if (_slider != null && !Mathf.Approximately(_slider.value, _before))
                {
                    _slider.value = _before; // the user's own volume setting (PlayerPrefs) is always put back
                }
            }
        }

        private static IEnumerator MoveSlider(AutoplayVirtualInput _input, AutoplayJournal _journal, Slider _slider, Canvas _canvas, float _before)
        {
            var _corners = new Vector3[4];
            ((RectTransform)_slider.transform).GetWorldCorners(_corners);
            Camera _camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            Vector2 _min = RectTransformUtility.WorldToScreenPoint(_camera, _corners[0]);
            Vector2 _max = RectTransformUtility.WorldToScreenPoint(_camera, _corners[2]);
            Vector2 _point = new(Mathf.Lerp(_min.x, _max.x, _slider.normalizedValue > 0.5f ? 0.3f : 0.7f), (_min.y + _max.y) * 0.5f);

            yield return _input.MoveTo(_point, 0.25f);
            GameObject _hit = AutoplayUiLocator.TopHit(_point);
            string _where = string.Format(CultureInfo.InvariantCulture, "target={0} pos={1:0},{2:0} hit={3} mode=pointer",
                AutoplayUiLocator.PathOf(_slider.gameObject), _point.x, _point.y, AutoplayUiLocator.PathOf(_hit));
            if (_hit == null || !(_hit == _slider.gameObject || _hit.transform.IsChildOf(_slider.transform)))
            {
                _journal.Record("input.miss", $"menu-slider {_where} reason=occluded");
                _journal.Record("menu.slider", "miss");
                yield break;
            }
            yield return _input.Click();
            _journal.Record("input.click", $"menu-slider {_where}");
            yield return new WaitForSecondsRealtime(0.2f);
            bool _moved = !Mathf.Approximately(_slider.value, _before);
            _journal.Record("menu.slider", string.Format(CultureInfo.InvariantCulture, "{0} {1:0.00}->{2:0.00}", _moved ? "ok" : "miss", _before, _slider.value));
        }

        /// <summary>
        /// Rejected join: is the disconnect notification on top (its Dismiss button reached by the raycast, above the
        /// login screen), and does a click on Dismiss hide it? Journals <c>menu.notification-top</c> and
        /// <c>menu.notification-dismiss</c>.
        /// </summary>
        public static IEnumerator CheckRejectedNotification(AutoplayVirtualInput _input, AutoplayJournal _journal)
        {
            GameObject _handler = GameObject.Find("ClientDisconnectHandler");
            Transform _notification = _handler != null ? _handler.transform.Find("ClientDisconnectCanvas/Notification") : null;
            float _deadline = Time.realtimeSinceStartup + 3f;
            while ((_notification == null || !_notification.gameObject.activeInHierarchy) && Time.realtimeSinceStartup < _deadline)
            {
                yield return null;
                _handler = _handler != null ? _handler : GameObject.Find("ClientDisconnectHandler");
                _notification = _handler != null ? _handler.transform.Find("ClientDisconnectCanvas/Notification") : null;
            }
            if (_notification == null || !_notification.gameObject.activeInHierarchy)
            {
                _journal.Record("menu.notification-top", "miss reason=not-shown");
                yield break;
            }

            Button _dismiss = _notification.GetComponentsInChildren<Button>().FirstOrDefault(_b => _b.name.IndexOf("Dismiss", StringComparison.OrdinalIgnoreCase) >= 0);
            AutoplayUiLocator.Probe _probe = AutoplayUiLocator.Locate(_dismiss ? _dismiss.gameObject : null);
            bool _onTop = _probe.found && string.IsNullOrEmpty(_probe.reason);
            _journal.Record("menu.notification-top", $"{(_onTop ? "ok" : "miss")} {_probe.Describe(_dismiss ? _dismiss.gameObject : null)}");
            if (!_onTop)
            {
                yield break;
            }

            bool _hidden = false;
            yield return Click(_input, _journal, "menu-notification-dismiss", _dismiss.gameObject,
                () => _notification == null || !_notification.gameObject.activeInHierarchy, _ok => _hidden = _ok);
            _journal.Record("menu.notification-dismiss", _hidden ? "ok" : "miss");
        }

        /// <summary>Clicks a uGUI object with the virtual pointer when it is the first thing under it, then waits (2 s) for
        /// the effect. Journals input.click / input.miss.</summary>
        public static IEnumerator Click(AutoplayVirtualInput _input, AutoplayJournal _journal, string _action, GameObject _target,
            Func<bool> _effect, Action<bool> _result)
        {
            AutoplayUiLocator.Probe _probe = AutoplayUiLocator.Locate(_target);
            if (!_probe.found || !string.IsNullOrEmpty(_probe.reason))
            {
                _journal.Record("input.miss", $"{_action} {_probe.Describe(_target)}");
                _result(false);
                yield break;
            }
            yield return _input.MoveTo(_probe.screen, 0.25f);
            yield return _input.Click();
            _journal.Record("input.click", $"{_action} {_probe.Describe(_target)} mode=pointer");
            float _deadline = Time.realtimeSinceStartup + 2f;
            while (!_effect())
            {
                if (Time.realtimeSinceStartup > _deadline)
                {
                    _journal.Record("input.miss", $"{_action} {_probe.Describe(_target)} reason=no-effect");
                    _result(false);
                    yield break;
                }
                yield return null;
            }
            _result(true);
        }
    }
}
#endif
