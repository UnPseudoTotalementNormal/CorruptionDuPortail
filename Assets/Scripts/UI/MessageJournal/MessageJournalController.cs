using Localization;
using CorruptionDuPortail.Domain.Localization;
using System.Collections.Generic;
using System.Linq;
using Avatars;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace UI.MessageJournal
{
    // Screen-space UITK overlay for the unified per-turn message journal. One surface, two triggers:
    // Open(animateNewest:true) presents it at the night reveal (the newest turn "writes in"), Open(false)
    // reopens it from the sacoche to browse. Enter/exit mirror RoleCardController (cdp-is-hidden /
    // cdp-is-collapsed + pickingMode). Data comes from an IMessageJournalDataSource (demo or live).
    [RequireComponent(typeof(UIDocument))]
    public class MessageJournalController : MonoBehaviour
    {
        private const string HiddenClass = "cdp-is-hidden";
        private const string CollapsedClass = "cdp-is-collapsed";
        private const int ExitCollapseDelayMs = 300;
        private const int WriteFadeMs = 320;
        private const int WriteStaggerMs = 140;
        private const float PastTurnOpacity = 0.6f;

        // Rich-text hexes mirror the --cdp-color-journal-* tokens (UITK inline rich text can't read var()).
        private const string NumberHex = "#EEC97A";
        private const string CorruptHex = "#DB4A56";
        private const string RobotHex = "#E2963A";
        private const string SepHex = "#50403C";

        [SerializeField] private UIDocument document;
        [SerializeField] private MonoBehaviour dataSourceBehaviour;
        // T23: opened from the sacoche in the seated first person, the journal frees the cursor and freezes the look
        // (like the pause menu), else nothing can close it and the view turns under it. Null-tolerant.
        [SerializeField] private AvatarCameraArbiter arbiter;

        private IMessageJournalDataSource _dataSource;
        private VisualElement _root;
        private ScrollView _scroll;
        private VisualElement _turns;
        private bool _initialized;
        private bool _subscribed;
        private bool _animateReveal;
        private int _revealAnimatedDay = int.MinValue; // newest day already write-in-animated this reveal (anti-flicker latch)
        private bool _canDismiss;
        private bool _isOpen;

        private void OnEnable()
        {
            Loc.LanguageChanged += OnLanguageChanged;
            TryInitialize();
            Subscribe();
        }

        // T36: the title comes from the UXML, every turn is written again.
        private void OnLanguageChanged()
        {
            if (!_initialized) return;
            LocalizedUitk.Localize(_root);
            Rebuild();
        }

        private void Start() => TryInitialize();

        private void OnDisable()
        {
            Loc.LanguageChanged -= OnLanguageChanged;
            Unsubscribe();
            arbiter?.SetOverlayOpen(this, false);
        }

        // Escape closes the browsed journal (the click outside needs a cursor); the night reveal stays non-skippable.
        private void Update()
        {
            if (_canDismiss && _isOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Diagnostics.Logs.GameLog.Info("ui", "ui.journal.escape");
                Close();
            }
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }
            if (document == null)
            {
                document = GetComponent<UIDocument>();
            }
            VisualElement _tree = document != null ? document.rootVisualElement : null;
            _root = _tree?.Q<VisualElement>("message-journal");
            if (_root == null)
            {
                return; // UIDocument tree not built yet — retried from the other entry point
            }

            _scroll = _root.Q<ScrollView>("journal-scroll");
            _turns = _root.Q<VisualElement>("journal-turns");
            _root.pickingMode = PickingMode.Ignore;
            _root.RegisterCallback<PointerDownEvent>(OnRootPointerDown);
            LocalizedUitk.Localize(_root); // the title written in MessageJournal.uxml

            _dataSource = dataSourceBehaviour as IMessageJournalDataSource;
            _initialized = true;
            Subscribe();
            Rebuild();
        }

        private void Subscribe()
        {
            if (_dataSource == null || _subscribed)
            {
                return;
            }
            _dataSource.OnChanged += OnDataChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_dataSource == null || !_subscribed)
            {
                return;
            }
            _dataSource.OnChanged -= OnDataChanged;
            _subscribed = false;
        }

        // _dismissible: night reveal passes false (non-skippable, timed like the legacy AwakeningRecapMessages —
        // a click outside must NOT close it); the sacoche browse passes true (click-outside dismisses).
        public void Open(bool _animateNewest, bool _dismissible)
        {
            TryInitialize();
            if (_root == null)
            {
                Diagnostics.Logs.GameLog.Warn("ui", "ui.journal.refused", "reason", "no-root");
                return;
            }
            Diagnostics.Logs.GameLog.Info("ui", "ui.journal.open", "kind", _dismissible ? "browse" : "reveal");
            _canDismiss = _dismissible;
            _animateReveal = _animateNewest;
            _revealAnimatedDay = int.MinValue; // fresh reveal: let the newest turn write in once
            _root.RemoveFromClassList(CollapsedClass);
            _root.pickingMode = PickingMode.Position;
            _isOpen = true;
            arbiter?.SetOverlayOpen(this, true);
            Rebuild();
            _root.schedule.Execute(() => _root.RemoveFromClassList(HiddenClass));
        }

        public void Close()
        {
            if (_root == null)
            {
                return;
            }
            if (!_root.ClassListContains(HiddenClass)) Diagnostics.Logs.GameLog.Info("ui", "ui.journal.close");
            _animateReveal = false;
            _root.AddToClassList(HiddenClass);
            _root.pickingMode = PickingMode.Ignore;
            _isOpen = false;
            arbiter?.SetOverlayOpen(this, false);
            _root.schedule.Execute(CollapseIfHidden).ExecuteLater(ExitCollapseDelayMs);
        }

        // Argument-less entry points (UnityEvent-wireable): the sacoche button binds to OpenForBrowse;
        // the awakening recap event calls OpenForReveal (non-dismissible, newest turn writes in).
        public void OpenForReveal() => Open(true, false);

        public void OpenForBrowse() => Open(false, true);

        private void CollapseIfHidden()
        {
            if (_root.ClassListContains(HiddenClass))
            {
                _root.AddToClassList(CollapsedClass);
            }
        }

        private void OnRootPointerDown(PointerDownEvent _evt)
        {
            if (_canDismiss && _evt.target == _root)
            {
                Close();
            }
        }

        private void OnDataChanged() => Rebuild();

        private void Rebuild()
        {
            if (!_initialized || _dataSource == null || _turns == null)
            {
                return;
            }
            _turns.Clear();

            IReadOnlyList<JournalTurnView> _views = _dataSource.GetTurns();
            int _lastIndex = _views.Count - 1;
            for (int _i = 0; _i < _views.Count; _i++)
            {
                bool _isNewest = _i == _lastIndex;
                VisualElement _entry = BuildTurn(_views[_i], _isNewest);
                _entry.style.opacity = _isNewest ? 1f : PastTurnOpacity;
                _turns.Add(_entry);
            }

            ScrollToBottom();

            // In reveal mode the newest turn writes in ONCE, the first time its day is the newest — not on every
            // rebuild. The turn's messages replicate as several OnListChanged events (one per message), independent
            // of the recap ShowEvent RPC; re-animating on each would re-blank + re-fade already-shown lines, a
            // visible flicker. Latching per-day keeps the "written on" feel without the stutter (messages that
            // arrive later for the same day pop in without a re-fade). Browse mode stays static.
            if (_animateReveal && _lastIndex >= 0 && _views[_lastIndex].day != _revealAnimatedDay)
            {
                _revealAnimatedDay = _views[_lastIndex].day;
                AnimateWriteIn(_turns[_lastIndex]);
            }
        }

        private VisualElement BuildTurn(JournalTurnView _turn, bool _isNewest)
        {
            var _entry = new VisualElement();
            _entry.AddToClassList("journal__turn");

            var _div = new VisualElement();
            _div.AddToClassList("journal__turn-div");
            var _lineLeft = new VisualElement();
            _lineLeft.AddToClassList("journal__turn-line");
            var _label = new Label(Loc.Tr("TOUR {0}", _turn.day));
            _label.AddToClassList("journal__turn-label");
            if (_isNewest)
            {
                _label.style.color = new StyleColor(new Color(0.941f, 0.906f, 0.867f));
            }
            var _lineRight = new VisualElement();
            _lineRight.AddToClassList("journal__turn-line");
            _div.Add(_lineLeft);
            _div.Add(_label);
            _div.Add(_lineRight);
            _entry.Add(_div);

            var _stat = new Label(BuildStatText(_turn));
            _stat.AddToClassList("journal__stat");
            _entry.Add(_stat);

            foreach (string _message in _turn.messages)
            {
                var _msg = new Label(Loc.Tr("« {0} »", _message)); // the quotation marks of the language
                _msg.AddToClassList("journal__msg");
                _entry.Add(_msg);
            }
            return _entry;
        }

        private static string BuildStatText(JournalTurnView _turn)
        {
            // T36: one source per whole clause ({2}, {3} = the colours), so each language orders its own words.
            string _corrupt = Loc.Tr(
                Loc.IsSingular(Loc.Language, _turn.corruptedCount)
                    ? "<color={2}>{0}/{1}</color> <color={3}>corrompu</color>" // loc
                    : "<color={2}>{0}/{1}</color> <color={3}>corrompus</color>", // loc
                _turn.corruptedCount, _turn.nonAnomalyTotal, NumberHex, CorruptHex);
            if (!_turn.hasRobot)
            {
                return _corrupt;
            }
            return $"{_corrupt}<color={SepHex}>   |   </color>{RobotClause(_turn.robotTargetCount)}";
        }

        private static string RobotClause(int _count)
        {
            if (_count <= 0)
            {
                return Loc.Tr("<color={0}>Personne</color> n'a ciblé le <color={1}>Robot</color>", NumberHex, RobotHex);
            }
            return Loc.Tr(
                Loc.IsSingular(Loc.Language, _count)
                    ? "<color={1}>{0}</color> joueur a ciblé le <color={2}>Robot</color>" // loc
                    : "<color={1}>{0}</color> joueurs ont ciblé le <color={2}>Robot</color>", // loc
                _count, NumberHex, RobotHex);
        }

        private void AnimateWriteIn(VisualElement _entry)
        {
            List<VisualElement> _children = _entry.Children().ToList();
            // Keep the divider (index 0) static; the stat + messages fade in staggered ("written on").
            for (int _i = 1; _i < _children.Count; _i++)
            {
                VisualElement _child = _children[_i];
                _child.style.opacity = 0f;
                int _delay = (_i - 1) * WriteStaggerMs;
                _entry.schedule.Execute(() =>
                    _child.experimental.animation
                        .Start(0f, 1f, WriteFadeMs, (_e, _v) => _e.style.opacity = _v)
                        .Ease(Easing.OutCubic)
                ).ExecuteLater(_delay);
            }
        }

        private void ScrollToBottom()
        {
            if (_scroll == null)
            {
                return;
            }
            _scroll.schedule.Execute(() =>
            {
                float _high = _scroll.verticalScroller.highValue;
                if (!float.IsNaN(_high))
                {
                    _scroll.verticalScroller.value = _high;
                }
            }).ExecuteLater(1);
        }
    }
}
