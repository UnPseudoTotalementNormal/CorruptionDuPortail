#if CDP_PLAYTEST_LOGS
using System;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Avatars;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Diagnostics.Logs
{
    /// <summary>
    /// Playtest builds only: F8 (or « Signaler un bug » in the pause menu) captures the screen, then opens
    /// « Qu'est-ce qui s'est passé ? » (mockups signaler-un-bug v1: window B2 = the pause menu's black panel,
    /// confirmation C1 « Envoyé, merci », access K2). Envoyer queues the report (<see cref="PlaytestLogRuntime.QueueReport"/>),
    /// closes the window at once and tries to send it for a few seconds while a small note (which never takes clicks)
    /// says how it went. Built in code on its own overlay canvas: no prefab, no scene change, nothing in a public build.
    /// The game is never paused (network game); while the window is open the camera arbiter frees the cursor and
    /// freezes the look, like the tablet.
    /// Test switch: <c>-playtest-bug-report-at &lt;seconds&gt;</c> sends one report on its own after that delay.
    /// </summary>
    internal sealed class BugReportWindow : MonoBehaviour
    {
        public const string AutoReportSwitch = "-playtest-bug-report-at";
        private const string Title = "Qu'est-ce qui s'est passé ?";
        private const string SendLabel = "Envoyer";
        private const string CancelLabel = "Annuler";
        private const string SentNote = "Envoyé, merci";
        private const string NotSentNote = "Pas envoyé";
        private const float SentNoteSeconds = 2f;
        private const float NotSentNoteSeconds = 3f;
        private const float SendNowSeconds = 8f;
        private const string SendingNote = "Envoi…";
        private static readonly Color PanelColor = new(0.043f, 0.043f, 0.043f, 0.96f);
        private static readonly Color ButtonColor = new(0.227f, 0.227f, 0.227f, 1f);
        private static readonly Color DimColor = new(0f, 0f, 0f, 0.28f);

        private PlaytestLogRuntime _runtime;
        private GameObject _root;
        private GameObject _panel;
        private GameObject _note;
        private TextMeshProUGUI _noteText;
        private RawImage _preview;
        private TMP_InputField _input;
        private Button _send;
        private Button _cancel;
        private TextMeshProUGUI _sendText;
        private Texture2D _capture;
        private byte[] _jpg;
        private bool _busy;
        private CursorLockMode _savedLock;
        private bool _savedVisible;
        private AvatarCameraArbiter _arbiter;
        private GameObject _ownEventSystem;

        private bool IsOpen => _root != null && _root.activeSelf;

        public static void Create(PlaytestLogRuntime _runtime, string[] _args)
        {
            var _go = new GameObject("[BugReport]") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(_go);
            var _window = _go.AddComponent<BugReportWindow>();
            _window._runtime = _runtime;
            _window.Build();
            SceneManager.sceneLoaded += _window.OnSceneLoaded;
            int _at = Array.IndexOf(_args, AutoReportSwitch);
            if (_at >= 0 && _at + 1 < _args.Length && float.TryParse(_args[_at + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float _delay))
            {
                _window.AutoReport(_delay, _window.GetCancellationTokenOnDestroy()).Forget();
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_capture != null) Destroy(_capture);
        }

        private void Update()
        {
            Keyboard _keyboard = Keyboard.current;
            if (_keyboard != null && !_busy)
            {
                if (_keyboard.f8Key.wasPressedThisFrame)
                {
                    if (IsOpen) Close();
                    else Open(null).Forget();
                }
                else if (IsOpen && _keyboard.escapeKey.wasPressedThisFrame)
                {
                    Close();
                }
            }
            if (IsOpen && _send != null && !_busy)
            {
                long _wait = _runtime.ReportWaitMs();
                _send.interactable = _wait <= 0;
                _sendText.text = _wait <= 0 ? SendLabel : $"{SendLabel} ({(_wait + 999) / 1000})";
            }
        }

        private void LateUpdate()
        {
            if (!IsOpen || _arbiter != null) return;
            // No camera arbiter (menus): keep the cursor free ourselves while the window needs it.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // ---- open / close ----

        /// <summary>Captures the screen (the pause menu hidden for that frame if it is the way in), then shows the window.</summary>
        private async UniTaskVoid Open(CanvasGroup _hideWhileCapturing)
        {
            if (_busy || IsOpen) return;
            _busy = true;
            float _alpha = _hideWhileCapturing != null ? _hideWhileCapturing.alpha : 1f;
            try
            {
                if (_hideWhileCapturing != null)
                {
                    _hideWhileCapturing.alpha = 0f;
                    await UniTask.NextFrame(this.GetCancellationTokenOnDestroy());
                }
                await UniTask.WaitForEndOfFrame(this, this.GetCancellationTokenOnDestroy());
                TakeCapture();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception _e)
            {
                GameLog.Warn("log", "log.report.capture.failed", "error", _e.Message);
                _jpg = null;
            }
            finally
            {
                if (_hideWhileCapturing != null) _hideWhileCapturing.alpha = _alpha; // never leave the menu invisible
                _busy = false;
            }
            Show();
        }

        private void TakeCapture()
        {
            Texture2D _screen = ScreenCapture.CaptureScreenshotAsTexture();
            if (_screen == null) throw new InvalidOperationException("no capture");
            (int _w, int _h) = BugReport.CaptureSize(_screen.width, _screen.height);
            if (_capture != null) Destroy(_capture);
            if (_w == _screen.width)
            {
                _capture = _screen;
            }
            else
            {
                RenderTexture _rt = RenderTexture.GetTemporary(_w, _h, 0);
                Graphics.Blit(_screen, _rt);
                RenderTexture _previous = RenderTexture.active;
                RenderTexture.active = _rt;
                _capture = new Texture2D(_w, _h, TextureFormat.RGB24, false);
                _capture.ReadPixels(new Rect(0, 0, _w, _h), 0, 0);
                _capture.Apply();
                RenderTexture.active = _previous;
                RenderTexture.ReleaseTemporary(_rt);
                Destroy(_screen);
            }
            _jpg = _capture.EncodeToJPG(75);
            _preview.texture = _capture;
            var _fitter = _preview.GetComponent<AspectRatioFitter>();
            _fitter.aspectRatio = _capture.width / (float)Mathf.Max(1, _capture.height);
        }

        private void Show()
        {
            EnsureEventSystem();
            _savedLock = Cursor.lockState;
            _savedVisible = Cursor.visible;
            _arbiter = FindAnyObjectByType<AvatarCameraArbiter>();
            _arbiter?.SetOverlayOpen(this, true);
            _input.text = string.Empty;
            _note.SetActive(false);
            _send.interactable = true;
            _cancel.interactable = true;
            _root.SetActive(true);
            _input.ActivateInputField();
            GameLog.Info("ui", "report.open");
        }

        private void Close()
        {
            if (!IsOpen) return;
            _root.SetActive(false);
            if (_arbiter != null)
            {
                _arbiter.SetOverlayOpen(this, false); // the camera re-applies what its current mode wants
                _arbiter = null;
            }
            else
            {
                Cursor.lockState = _savedLock;
                Cursor.visible = _savedVisible;
            }
            if (_ownEventSystem != null)
            {
                Destroy(_ownEventSystem);
                _ownEventSystem = null;
            }
        }

        private async UniTaskVoid Submit()
        {
            if (_busy) return;
            _busy = true;
            string _id = _runtime.QueueReport(_input.text, _jpg);
            Close(); // the player is back in the game at once; the note below never takes clicks
            _noteText.text = SendingNote;
            _note.SetActive(true);
            bool _sent;
            try
            {
                _sent = _id != null && await _runtime.TrySendNow(_id, SendNowSeconds);
            }
            catch (Exception)
            {
                _sent = false;
            }
            _noteText.text = _sent ? SentNote : NotSentNote;
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_sent ? SentNoteSeconds : NotSentNoteSeconds), DelayType.Realtime,
                    PlayerLoopTiming.Update, this.GetCancellationTokenOnDestroy());
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                _busy = false;
            }
            if (!IsOpen) _note.SetActive(false);
        }

        private async UniTaskVoid AutoReport(float _delay, CancellationToken _ct)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_delay), DelayType.Realtime, PlayerLoopTiming.Update, _ct);
            Open(null).Forget();
            await UniTask.WaitUntil(() => IsOpen, cancellationToken: _ct);
            _input.text = "Signalement de test (autoplay) : élu, vote « perdu », ça marche";
            Submit().Forget();
        }

        // A screen without an EventSystem (rare: between scenes) would leave the buttons dead.
        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            _ownEventSystem = new GameObject("[BugReport EventSystem]", typeof(EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            _ownEventSystem.transform.SetParent(transform, false);
        }

        // ---- pause menu line (K2) ----

        private void OnSceneLoaded(Scene _scene, LoadSceneMode _mode)
        {
            try
            {
                foreach (GameObject _rootObject in _scene.GetRootGameObjects())
                {
                    foreach (LeaveGameButton _leave in _rootObject.GetComponentsInChildren<LeaveGameButton>(true))
                    {
                        if (_leave.transform.parent == null || _leave.transform.parent.name != "PausePanel") continue;
                        AddMenuLine(_leave);
                    }
                }
            }
            catch (Exception _e)
            {
                GameLog.Warn("log", "log.report.menu.failed", "error", _e.Message); // F8 still works
            }
        }

        /// <summary>« Signaler un bug » under « Quitter la partie »: a copy of that button, without its leave behaviour.</summary>
        private void AddMenuLine(LeaveGameButton _leave)
        {
            Transform _panel = _leave.transform.parent;
            if (_panel.Find("ReportBugButton") != null) return;
            var _holder = new GameObject("[ReportBugButton holder]");
            _holder.SetActive(false); // the copy is created inactive: the leave button's Awake never runs on it
            GameObject _line = Instantiate(_leave.gameObject, _holder.transform);
            DestroyImmediate(_line.GetComponent<LeaveGameButton>());
            var _button = _line.GetComponent<CustomButton>();
            _button.onButtonClickedUnityEvent = new UnityEvent();
            _line.name = "ReportBugButton";
            Text _label = _line.GetComponentInChildren<Text>(true);
            if (_label != null) _label.text = "Signaler un bug";
            var _source = (RectTransform)_leave.transform;
            var _rect = (RectTransform)_line.transform;
            _line.transform.SetParent(_panel, false);
            _line.transform.SetSiblingIndex(_leave.transform.GetSiblingIndex() + 1);
            _rect.anchoredPosition = _source.anchoredPosition - new Vector2(0f, _source.rect.height + 12f);
            Destroy(_holder);
            CanvasGroup _menu = _panel.GetComponent<CanvasGroup>();
            _button.onButtonClicked += () => Open(_menu).Forget();
        }

        // ---- UI (B2) ----

        private void Build()
        {
            var _canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);
            var _canvas = _canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000; // above every game canvas and UI Toolkit panel
            var _scaler = _canvasGo.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1600, 900);
            _scaler.matchWidthOrHeight = 0.5f;

            _root = NewUi("Window", _canvasGo.transform);
            Stretch(_root);
            var _dim = _root.AddComponent<Image>();
            _dim.color = DimColor; // also blocks clicks to the game while the window is open

            _panel = NewUi("Panel", _root.transform);
            var _panelRect = (RectTransform)_panel.transform;
            _panelRect.sizeDelta = new Vector2(560, 0);
            _panel.AddComponent<Image>().color = PanelColor;
            var _layout = _panel.AddComponent<VerticalLayoutGroup>();
            _layout.padding = new RectOffset(28, 28, 22, 26);
            _layout.spacing = 16;
            _layout.childControlWidth = true;
            _layout.childControlHeight = true;
            _layout.childForceExpandHeight = false;
            _panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            NewText("Title", _panel.transform, Title, 32, Color.white, TextAlignmentOptions.Center);

            GameObject _previewGo = NewUi("Capture", _panel.transform);
            _preview = _previewGo.AddComponent<RawImage>();
            var _fitter = _previewGo.AddComponent<AspectRatioFitter>();
            _fitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            _fitter.aspectRatio = 16f / 9f;
            _previewGo.AddComponent<LayoutElement>().preferredHeight = 282;

            _input = NewInput(_panel.transform);

            GameObject _row = NewUi("Buttons", _panel.transform);
            var _rowLayout = _row.AddComponent<HorizontalLayoutGroup>();
            _rowLayout.spacing = 16;
            _rowLayout.childControlWidth = true;
            _rowLayout.childControlHeight = true;
            _rowLayout.childForceExpandWidth = true;
            _row.AddComponent<LayoutElement>().preferredHeight = 46;
            _cancel = NewButton(_row.transform, CancelLabel, out _);
            _send = NewButton(_row.transform, SendLabel, out _sendText);
            _cancel.onClick.AddListener(Close);
            _send.onClick.AddListener(() => Submit().Forget());

            _note = NewUi("Note", _canvasGo.transform);
            ((RectTransform)_note.transform).sizeDelta = new Vector2(400, 96);
            var _noteImage = _note.AddComponent<Image>();
            _noteImage.color = PanelColor;
            _noteImage.raycastTarget = false; // never in the way of the game
            _noteText = NewText("Text", _note.transform, SentNote, 32, Color.white, TextAlignmentOptions.Center);
            Stretch(_noteText.gameObject);

            _root.SetActive(false);
            _note.SetActive(false);
        }

        private static GameObject NewUi(string _name, Transform _parent)
        {
            var _go = new GameObject(_name, typeof(RectTransform));
            _go.transform.SetParent(_parent, false);
            return _go;
        }

        private static void Stretch(GameObject _go)
        {
            var _rect = (RectTransform)_go.transform;
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI NewText(string _name, Transform _parent, string _text, float _size, Color _color,
            TextAlignmentOptions _align)
        {
            var _tmp = NewUi(_name, _parent).AddComponent<TextMeshProUGUI>();
            _tmp.text = _text;
            _tmp.fontSize = _size;
            _tmp.color = _color;
            _tmp.alignment = _align;
            _tmp.raycastTarget = false;
            return _tmp;
        }

        private static TMP_InputField NewInput(Transform _parent)
        {
            GameObject _box = NewUi("Text", _parent);
            _box.AddComponent<Image>().color = Color.white;
            _box.AddComponent<LayoutElement>().preferredHeight = 110;
            GameObject _area = NewUi("Text Area", _box.transform);
            Stretch(_area);
            var _areaRect = (RectTransform)_area.transform;
            _areaRect.offsetMin = new Vector2(12, 8);
            _areaRect.offsetMax = new Vector2(-12, -8);
            _area.AddComponent<RectMask2D>();
            TextMeshProUGUI _placeholder = NewText("Placeholder", _area.transform, string.Empty, 24,
                new Color(0.45f, 0.45f, 0.45f), TextAlignmentOptions.TopLeft);
            Stretch(_placeholder.gameObject);
            TextMeshProUGUI _text = NewText("Text", _area.transform, string.Empty, 24, new Color(0.07f, 0.07f, 0.07f),
                TextAlignmentOptions.TopLeft);
            Stretch(_text.gameObject);
            var _input = _box.AddComponent<TMP_InputField>();
            _input.textViewport = _areaRect;
            _input.textComponent = _text;
            _input.placeholder = _placeholder;
            _input.lineType = TMP_InputField.LineType.MultiLineNewline;
            _input.characterLimit = BugReport.MaxTextChars;
            _input.richText = false;
            _input.restoreOriginalTextOnEscape = false; // Échap closes the window, it never empties the text
            return _input;
        }

        private static Button NewButton(Transform _parent, string _label, out TextMeshProUGUI _text)
        {
            GameObject _go = NewUi(_label, _parent);
            var _image = _go.AddComponent<Image>();
            _image.color = ButtonColor;
            var _button = _go.AddComponent<Button>();
            _button.targetGraphic = _image;
            _text = NewText("Label", _go.transform, _label, 24, Color.white, TextAlignmentOptions.Center);
            Stretch(_text.gameObject);
            return _button;
        }
    }
}
#endif
