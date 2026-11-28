using System;
using Diagnostics.Logs;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI.Settings
{
    /// <summary>
    /// The options in several menus (mockups micro v2, O1): Audio, Vocal, Affichage and Essais written at the top of the
    /// options in place of their title, the open one in gold; a click shows its rows and hides the others'. In the main
    /// menu and the pause menu (the same AudioPanel prefab). Essais lives in the pause menu's scene (the seated view
    /// trials): without its page the word is hidden. The options open on the last menu looked at, else Audio.
    /// </summary>
    public class OptionsTabs : MonoBehaviour
    {
        [Serializable]
        public class Tab
        {
            public string logName;
            public TMP_Text word;
            [Tooltip("The rows of the panel this menu shows.")]
            public GameObject[] rows;
            [Tooltip("A page outside the panel (Essais: the pause menu's trial panel). Hidden word when the menu has no row and no page.")]
            public GameObject page;
        }

        [SerializeField] private Tab[] _tabs;
        [SerializeField] private Color _openColor = new(0.941f, 0.722f, 0.353f); // #F0B85A
        [SerializeField] private Color _closedColor = new(0.945f, 0.886f, 0.769f, 0.7f); // #F1E2C4, dimmed

        private static int s_lastOpen;
        private int _open;
        private int _hovered = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_lastOpen = 0;

        private void Awake()
        {
            Assert.IsTrue(_tabs != null && _tabs.Length > 0, "OptionsTabs._tabs not wired");
            for (int i = 0; i < _tabs.Length; i++)
            {
                int _index = i;
                Tab _tab = _tabs[i];
                Assert.IsNotNull(_tab.word, $"OptionsTabs tab {i} has no word");
                bool _empty = (_tab.rows == null || _tab.rows.Length == 0) && _tab.page == null;
                _tab.word.gameObject.SetActive(!_empty);
                Button _button = _tab.word.GetComponent<Button>();
                if (_button != null)
                {
                    _button.onClick.AddListener(() => Open(_index));
                }
                EventTrigger _trigger = _tab.word.GetComponent<EventTrigger>() ?? _tab.word.gameObject.AddComponent<EventTrigger>();
                AddTrigger(_trigger, EventTriggerType.PointerEnter, () => { _hovered = _index; Paint(); });
                AddTrigger(_trigger, EventTriggerType.PointerExit, () => { if (_hovered == _index) _hovered = -1; Paint(); });
            }
        }

        private static void AddTrigger(EventTrigger _trigger, EventTriggerType _type, Action _action)
        {
            var _entry = new EventTrigger.Entry { eventID = _type };
            _entry.callback.AddListener(_ => _action());
            _trigger.triggers.Add(_entry);
        }

        private void OnEnable()
        {
            _hovered = -1;
            int _start = s_lastOpen < _tabs.Length && _tabs[s_lastOpen].word.gameObject.activeSelf ? s_lastOpen : 0;
            Apply(_start);
        }

        // The panel is hidden (options closed, menu left): a page outside it goes with it.
        private void OnDisable()
        {
            foreach (Tab _tab in _tabs)
            {
                if (_tab.page != null)
                {
                    _tab.page.SetActive(false);
                }
            }
        }

        private void Open(int _index)
        {
            if (_index == _open)
            {
                return;
            }
            GameLog.Info("ui", "ui.options.tab", "menu", _tabs[_index].logName, "from", _tabs[_open].logName);
            s_lastOpen = _index;
            Apply(_index);
        }

        private void Apply(int _index)
        {
            _open = _index;
            // hide first: a row shared by two menus stays shown
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (i != _index) Show(_tabs[i], false);
            }
            Show(_tabs[_index], true);
            Paint();
        }

        private static void Show(Tab _tab, bool _on)
        {
            if (_tab.rows != null)
            {
                foreach (GameObject _row in _tab.rows)
                {
                    if (_row != null) _row.SetActive(_on);
                }
            }
            if (_tab.page != null) _tab.page.SetActive(_on);
        }

        private void Paint()
        {
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].word.color = i == _open || i == _hovered ? _openColor : _closedColor;
            }
        }
    }
}
