using System.Collections.Generic;
using Avatars;
using CorruptionDuPortail.Domain;
using Smartphone;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VoiceSystem;

namespace UI.KeyHints
{
    /// <summary>
    /// The keyboard keys are invisible to the player (2026-10-21): a small drawing with its key under it, no word, half
    /// lit, for each key that does something right now (mockups touches v1 A3 + C1, v2 L2 + B1 + K5). Which ones:
    /// <see cref="CorruptionDuPortail.Domain.KeyHints"/>. Where (mockups touches-jour v1 A2 + C2): the menu and the bug
    /// report top left, in place of the menu's grey square (made invisible, it still takes the click), the others in a
    /// column under them, the vote's keys first then a gap. Key names follow the player's keyboard layout. Local
    /// presentation only, on its own overlay canvas built in code (like the night parchment): it never takes a click.
    /// Hidden while the pause menu is open.
    ///
    /// Also "Clic : voter" under the seated reticle while it is on a cat a click would vote for (touches-jour v1 D1).
    /// </summary>
    public class KeyHintsHud : MonoBehaviour
    {
        // Mockup sizes on a 1600 x 900 screen.
        private static readonly Vector2 ReferenceResolution = new(1600f, 900f);
        private static readonly Vector2 CornerOffset = new(14f, -12f);
        private static readonly Vector2 ColumnOffset = new(22f, -110f);
        private static readonly Vector2 IconSize = new(46f, 38f);
        private const float CornerSpacing = 14f;
        private const float ColumnSpacing = 10f;
        private const float GroupGap = 56f; // between the vote's keys and the others
        private const float HintMinWidth = 58f;
        private const float IconLabelSpacing = 5f;
        private const int LabelFontSize = 19;
        private static readonly Color InkColor = new(0.918f, 0.863f, 0.749f, 1f); // #eadcbf
        private static readonly Color ShadowColor = new(0f, 0f, 0f, 0.8f);
        // "Clic : voter": just under the reticle, fully lit (it is read at the moment of aiming).
        private const string VoteAimText = "Clic : voter";
        private static readonly Vector2 VoteAimOffset = new(0f, -37f);
        private const int VoteAimFontSize = 26;
        private static readonly Color VoteAimColor = new(1f, 0.953f, 0.855f, 1f); // #fff3da
        private const float VoteAimFadeSeconds = 0.2f;

        // Lane A: wired in GameScene. The voice and the emote wheel may be missing (their hint then never shows).
        [SerializeField] private SmartphoneController _smartphone;
        [SerializeField] private TabletToggleInput _tabletToggle;
        [SerializeField] private VoiceChatManager _voice;
        [SerializeField] private EmoteWheelInput _emoteWheel;
        [SerializeField] private PauseMenuInput _pauseMenu;
        [Tooltip("T23: the day's screen rows (keys 1 and 2). Missing = their hints never show.")]
        [SerializeField] private Presentation.DayScreenRows _dayRows;
        [Tooltip("T23: the day's vote on the cats (its skip key). Missing = the skip hint never shows.")]
        [SerializeField] private Presentation.DayCatVoting _dayVoting;
        [Tooltip("The pause menu's grey square, top left: made invisible (the menu's drawing sits on it), it still opens the menu on a click.")]
        [SerializeField] private Graphic _pauseButtonGraphic;
        [Tooltip("Tells whether the seated reticle is live (cursor locked): the vote hint only shows under it then.")]
        [SerializeField] private AvatarCameraArbiter _arbiter;
        [SerializeField] private Font _font;
        // The game blends in linear space: a CanvasGroup alpha of 0.5 looks far brighter than the mockup's 50 % (a CSS
        // blend in sRGB). 0.5^2.2 = 0.22 gives the mockup's look over the dark corner.
        [Tooltip("Mockups touches v2 L2 (half lit) = 0.22 in linear space.")]
        [Range(0f, 1f)] [SerializeField] private float _opacity = 0.22f;
        [SerializeField] private int _sortingOrder = 5;

        [Header("Drawings, in KeyHintAction order")]
        [SerializeField] private Sprite _openTabletIcon;
        [SerializeField] private Sprite _closeTabletIcon;
        [SerializeField] private Sprite _previousScreenIcon;
        [SerializeField] private Sprite _nextScreenIcon;
        [SerializeField] private Sprite _speakIcon;
        [SerializeField] private Sprite _emotesIcon;
        [SerializeField] private Sprite _menuIcon;
        [SerializeField] private Sprite _reportBugIcon;
        [SerializeField] private Sprite _playersRowIcon;
        [SerializeField] private Sprite _rolesRowIcon;
        [SerializeField] private Sprite _skipVoteIcon;

        private readonly Dictionary<KeyHintAction, Hint> _hints = new();
        private readonly List<KeyHintAction> _shown = new();
        private readonly List<KeyHintAction> _wanted = new();
        private Canvas _canvas;
        private GameObject _groupGap;
        private CanvasGroup _voteAim;
        private bool _bugReportAvailable;

        private sealed class Hint
        {
            public GameObject Root;
            public Text Label;
            public System.Func<Key> KeyOf;
        }

        private void Awake()
        {
            Assert.IsNotNull(_smartphone, "KeyHintsHud._smartphone is not wired — wire the SmartphoneController in GameScene.");
            Assert.IsNotNull(_tabletToggle, "KeyHintsHud._tabletToggle is not wired — wire the TabletToggleInput in GameScene.");
            Assert.IsNotNull(_pauseMenu, "KeyHintsHud._pauseMenu is not wired — wire the PauseMenuInput in GameScene.");
            Assert.IsNotNull(_font, "KeyHintsHud._font is not wired — wire EB Garamond.");
            Build();
        }

        private void Start()
        {
            // A2: the menu's drawing replaces the grey square; the square stays as the click target.
            if (_pauseButtonGraphic != null) _pauseButtonGraphic.canvasRenderer.SetAlpha(0f);
#if CDP_PLAYTEST_LOGS
            // F8 exists only in playtest builds whose log switch is on (the window is made at startup then).
            _bugReportAvailable = FindAnyObjectByType<Diagnostics.Logs.BugReportWindow>() != null;
#endif
        }

        private void Update()
        {
            UpdateVoteAim();
            if (_pauseMenu.IsMenuOpen) _wanted.Clear();
            else CorruptionDuPortail.Domain.KeyHints.For(ReadContext(), _wanted);
            if (SameAsShown())
            {
                return;
            }
            _shown.Clear();
            _shown.AddRange(_wanted);
            foreach (KeyValuePair<KeyHintAction, Hint> _pair in _hints)
            {
                bool _on = _wanted.Contains(_pair.Key);
                if (_on) _pair.Value.Label.text = LabelOf(_pair.Value.KeyOf());
                _pair.Value.Root.SetActive(_on);
            }
            _groupGap.SetActive(HasGroup(KeyHintGroup.Vote) && HasGroup(KeyHintGroup.Other));
            _canvas.enabled = _wanted.Count > 0;
        }

        private bool HasGroup(KeyHintGroup _group)
        {
            foreach (KeyHintAction _action in _wanted)
            {
                if (CorruptionDuPortail.Domain.KeyHints.GroupOf(_action) == _group) return true;
            }
            return false;
        }

        private void UpdateVoteAim()
        {
            bool _on = _dayVoting != null && _arbiter != null && _arbiter.WantsLockedCursor && !_pauseMenu.IsMenuOpen
                       && _dayVoting.PointedVotable;
            _voteAim.alpha = Mathf.MoveTowards(_voteAim.alpha, _on ? 1f : 0f, Time.unscaledDeltaTime / VoteAimFadeSeconds);
        }

        private KeyHintContext ReadContext()
        {
            bool _open = _smartphone.IsOpen;
            return new KeyHintContext(
                tabletAvailable: true,
                tabletOpen: _open,
                hasPreviousScreen: _open && _smartphone.HasNeighborApp(SmartphoneController.SwipeDirection.Left),
                hasNextScreen: _open && _smartphone.HasNeighborApp(SmartphoneController.SwipeDirection.Right),
                canSpeak: _voice != null && _voice.PushToTalkReady,
                emotesReachable: _emoteWheel != null && _emoteWheel.isActiveAndEnabled && _emoteWheel.IsReachable,
                menuAvailable: true,
                bugReportAvailable: _bugReportAvailable,
                dayRowsAvailable: _dayRows != null && _dayRows.IsActive,
                skipVoteAvailable: _dayVoting != null && _dayVoting.SkipReachable);
        }

        private bool SameAsShown()
        {
            if (_wanted.Count != _shown.Count) return false;
            for (int _i = 0; _i < _wanted.Count; _i++)
            {
                if (_wanted[_i] != _shown[_i]) return false;
            }
            return true;
        }

        // What the player's keyboard prints on the key (an AZERTY layout renames letters).
        private static string LabelOf(Key _key)
        {
            Keyboard _keyboard = Keyboard.current;
            string _printed = _keyboard != null ? _keyboard[_key].displayName : null;
            return CorruptionDuPortail.Domain.KeyHints.Label(_key.ToString(), _printed);
        }

        // ---- build ----

        private void Build()
        {
            var _canvasObject = new GameObject("KeyHintsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            _canvasObject.transform.SetParent(transform, false);
            _canvas = _canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = _sortingOrder;
            _canvas.enabled = false;
            var _scaler = _canvasObject.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = ReferenceResolution;
            _scaler.matchWidthOrHeight = 0.5f;
            var _group = _canvasObject.GetComponent<CanvasGroup>();
            _group.alpha = _opacity;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            RectTransform _corner = NewGroup("Corner", _canvasObject.transform, CornerOffset, CornerSpacing, false);
            RectTransform _column = NewGroup("Column", _canvasObject.transform, ColumnOffset, ColumnSpacing, true);

            // Keys are read when a hint shows (the tablet's action binds in its own Awake). The column's order is the
            // layout's: the vote's keys, the gap, the others.
            AddHint(_column, KeyHintAction.PlayersRow, _playersRowIcon, () => Key.Digit1);
            AddHint(_column, KeyHintAction.RolesRow, _rolesRowIcon, () => Key.Digit2);
            AddHint(_column, KeyHintAction.SkipVote, _skipVoteIcon, () => _dayVoting.SkipKey);
            RectTransform _gap = NewRect("Gap", _column);
            _gap.gameObject.AddComponent<LayoutElement>().preferredHeight = GroupGap;
            _groupGap = _gap.gameObject;
            _groupGap.SetActive(false);
            AddHint(_column, KeyHintAction.OpenTablet, _openTabletIcon, () => _tabletToggle.ToggleKey);
            AddHint(_column, KeyHintAction.CloseTablet, _closeTabletIcon, () => _tabletToggle.ToggleKey);
            AddHint(_column, KeyHintAction.PreviousScreen, _previousScreenIcon, () => Key.LeftArrow);
            AddHint(_column, KeyHintAction.NextScreen, _nextScreenIcon, () => Key.RightArrow);
            AddHint(_column, KeyHintAction.Speak, _speakIcon, () => _voice.PushToTalkKey);
            AddHint(_column, KeyHintAction.Emotes, _emotesIcon, () => _emoteWheel.OpenKey);
            AddHint(_corner, KeyHintAction.Menu, _menuIcon, () => _pauseMenu.MenuKey);
            AddHint(_corner, KeyHintAction.ReportBug, _reportBugIcon, () => Key.F8);

            BuildVoteAim();
        }

        // Anchored top left: a row (the corner) or a column, each hint centred on its line.
        private static RectTransform NewGroup(string _name, Transform _parent, Vector2 _offset, float _spacing, bool _vertical)
        {
            RectTransform _group = NewRect(_name, _parent);
            _group.anchorMin = new Vector2(0f, 1f);
            _group.anchorMax = new Vector2(0f, 1f);
            _group.pivot = new Vector2(0f, 1f);
            _group.anchoredPosition = _offset;
            HorizontalOrVerticalLayoutGroup _layout = _vertical
                ? _group.gameObject.AddComponent<VerticalLayoutGroup>()
                : _group.gameObject.AddComponent<HorizontalLayoutGroup>();
            _layout.spacing = _spacing;
            _layout.childAlignment = _vertical ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
            _layout.childControlWidth = true;
            _layout.childControlHeight = true;
            _layout.childForceExpandWidth = false;
            _layout.childForceExpandHeight = false;
            var _fitter = _group.gameObject.AddComponent<ContentSizeFitter>();
            _fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            _fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return _group;
        }

        // On its own canvas: fully lit, unlike the half-lit hints.
        private void BuildVoteAim()
        {
            var _canvasObject = new GameObject("VoteAimCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            _canvasObject.transform.SetParent(transform, false);
            var _aimCanvas = _canvasObject.GetComponent<Canvas>();
            _aimCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _aimCanvas.sortingOrder = _sortingOrder;
            var _scaler = _canvasObject.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = ReferenceResolution;
            _scaler.matchWidthOrHeight = 0.5f;
            _voteAim = _canvasObject.GetComponent<CanvasGroup>();
            _voteAim.alpha = 0f;
            _voteAim.interactable = false;
            _voteAim.blocksRaycasts = false;

            RectTransform _labelRect = NewRect("VoteAim", _canvasObject.transform);
            _labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _labelRect.pivot = new Vector2(0.5f, 0.5f);
            _labelRect.anchoredPosition = VoteAimOffset;
            _labelRect.sizeDelta = new Vector2(400f, 40f);
            var _label = _labelRect.gameObject.AddComponent<Text>();
            _label.text = VoteAimText;
            _label.font = _font;
            _label.fontSize = VoteAimFontSize;
            _label.color = VoteAimColor;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.raycastTarget = false;
            AddShadow(_labelRect.gameObject);
        }

        private void AddHint(RectTransform _group, KeyHintAction _action, Sprite _icon, System.Func<Key> _keyOf)
        {
            RectTransform _root = NewRect(_action.ToString(), _group);
            _root.gameObject.AddComponent<LayoutElement>().minWidth = HintMinWidth;
            var _column = _root.gameObject.AddComponent<VerticalLayoutGroup>();
            _column.spacing = IconLabelSpacing;
            _column.childAlignment = TextAnchor.LowerCenter;
            _column.childControlWidth = true;
            _column.childControlHeight = true;
            _column.childForceExpandWidth = false;
            _column.childForceExpandHeight = false;

            RectTransform _iconRect = NewRect("Icon", _root);
            var _image = _iconRect.gameObject.AddComponent<Image>();
            _image.sprite = _icon;
            _image.color = Color.white; // the drawings are already in the ink colour
            _image.preserveAspect = true;
            _image.raycastTarget = false;
            var _size = _iconRect.gameObject.AddComponent<LayoutElement>();
            _size.preferredWidth = IconSize.x;
            _size.preferredHeight = IconSize.y;
            AddShadow(_iconRect.gameObject);

            RectTransform _labelRect = NewRect("Key", _root);
            var _label = _labelRect.gameObject.AddComponent<Text>();
            _label.font = _font;
            _label.fontSize = LabelFontSize;
            _label.color = InkColor;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.raycastTarget = false;
            AddShadow(_labelRect.gameObject);

            _root.gameObject.SetActive(false);
            _hints[_action] = new Hint { Root = _root.gameObject, Label = _label, KeyOf = _keyOf };
        }

        private static void AddShadow(GameObject _target)
        {
            var _shadow = _target.AddComponent<Shadow>();
            _shadow.effectColor = ShadowColor;
            _shadow.effectDistance = new Vector2(0f, -1.5f);
        }

        private static RectTransform NewRect(string _name, Transform _parent)
        {
            var _rect = (RectTransform)new GameObject(_name, typeof(RectTransform)).transform;
            _rect.SetParent(_parent, false);
            return _rect;
        }
    }
}
