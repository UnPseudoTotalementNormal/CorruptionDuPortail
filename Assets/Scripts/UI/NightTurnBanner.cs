using Characters;
using CorruptionDuPortail.Domain;
using Extensions;
using FMODUnity;
using GameLogic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// Board task T21 ("Noter quand c'est à nous / pas à nous de jouer"), mockups tour-de-nuit v5 C3 + v4 T2
    /// (2026-10-16): when the local player's role wakes at night, a parchment "Vous vous éveillez" unrolls across the
    /// table, stays, then catches fire and burns away in a second; "Vous vous rendormez" does the same when the
    /// player's turn ends. Replaces the 3 s "Vous vous éveillez..." text of <see cref="AwakeningStateUI"/>.
    ///
    /// Local presentation only, on its own overlay canvas (a state UI fades out with its state, and the turn can end
    /// with the night). The parchment, its seal and its text share one material (<c>CdP/UI Parchment Burn</c>), so
    /// one burn front runs through all three; the look of the burn is tuned on that material. Timing:
    /// <see cref="ParchmentBannerTimeline"/>.
    /// </summary>
    public class NightTurnBanner : MonoBehaviour
    {
        // Mockup size: 1060 x 120 on a 1600 x 900 screen, a little below the centre (over the table, between the rows).
        private static readonly Vector2 ReferenceResolution = new(1600f, 900f);
        private static readonly Vector2 BannerSize = new(1060f, 120f);
        private const float BannerY = -20f;
        private const float SealSize = 96f;
        private const float SealInset = 95f;   // seal centre from the right end
        private const float TextRightPad = 110f; // the text stays clear of the seal
        private const float RollerWidth = 22f;
        private const float RollerOverhang = 6f;

        private static readonly int BurnRectId = Shader.PropertyToID("_BurnRect");
        private static readonly int BurnId = Shader.PropertyToID("_Burn");
        private static readonly int UnrollId = Shader.PropertyToID("_Unroll");

        // Lane A: scene-wired in GameScene, like AwakeningLight.
        [SerializeField] private CharacterManager characterManager;
        private ICharacterQuery CharacterQuery => characterManager;
        [SerializeField] private GameManager gameManager;
        private IGameLoop Loop => gameManager;

        [Tooltip("CdP/UI Parchment Burn material: the burn's look (front, embers, char) is tuned there.")]
        [SerializeField] private Material burnMaterial;
        [Tooltip("The role book's parchment; its top band is used.")]
        [SerializeField] private Texture parchment;
        [Tooltip("The role book's wax seal (white mask, tinted).")]
        [SerializeField] private Texture seal;
        [SerializeField] private Color sealColor = new(0.55f, 0.1f, 0.07f, 1f);
        [Tooltip("IM Fell English, the role book's font.")]
        [SerializeField] private Font font;
        [SerializeField] private int fontSize = 68;
        [SerializeField] private Color textColor = new(0.227f, 0.141f, 0.078f, 1f);
        [SerializeField] private Color rollerColor = new(0.79f, 0.64f, 0.42f, 1f);

        [SerializeField] private string wakeText = "Vous vous éveillez";
        [SerializeField] private string sleepText = "Vous vous rendormez";
        [Tooltip("Seconds the wake parchment stays whole (unrolling included) before it burns.")]
        [SerializeField] private float wakeWholeSeconds = 2.5f;
        [Tooltip("Seconds the sleep parchment stays whole (unrolling included) before it burns.")]
        [SerializeField] private float sleepWholeSeconds = 1.8f;
        [SerializeField] private int sortingOrder = 10;
        [Tooltip("Optional: played when a parchment unrolls.")]
        [SerializeField] private EventReference unrollSound;
        [Tooltip("Optional: played when a parchment catches fire.")]
        [SerializeField] private EventReference burnSound;

        private Canvas canvas;
        private RectTransform canvasRect;
        private RectTransform banner;
        private Text label;
        private RectTransform leftRoller;
        private RectTransform rightRoller;
        private Material material;
        private readonly Vector3[] corners = new Vector3[4];

        private bool playing;
        private bool burning;
        private float elapsed;
        private float wholeSeconds;
        private string pendingText;
        private float pendingWholeSeconds;

        private Character subscribedCharacter;

        /// <summary>The text on screen right now, null when nothing shows (autoplay checks).</summary>
        public string ShownText => playing ? label.text : null;

        private void Awake()
        {
            Build();
        }

        private void Start()
        {
            Assert.IsNotNull(characterManager, "NightTurnBanner.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(gameManager, "NightTurnBanner.gameManager is not wired — wire it in GameScene (the composition root).");
            Loop.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            if (subscribedCharacter != null)
            {
                subscribedCharacter.isAwakened.OnValueChanged -= OnAwakeningChanged;
            }
            subscribedCharacter = CharacterQuery.GetLocalCharacter(false);
            subscribedCharacter.isAwakened.OnValueChanged += OnAwakeningChanged;
        }

        private void OnDestroy()
        {
            if (gameManager != null)
            {
                Loop.onGameStarted -= OnGameStarted;
            }
            if (subscribedCharacter != null)
            {
                subscribedCharacter.isAwakened.OnValueChanged -= OnAwakeningChanged;
            }
            if (material != null)
            {
                Destroy(material);
            }
        }

        private void OnAwakeningChanged(bool _previousValue, bool _newValue)
        {
            if (_newValue && !_previousValue)
            {
                Play(wakeText, wakeWholeSeconds);
            }
            else if (!_newValue && _previousValue)
            {
                Play(sleepText, sleepWholeSeconds);
            }
        }

        /// <summary>
        /// Unrolls a parchment with this text; it burns away after <paramref name="_wholeSeconds"/>. If one is still
        /// showing (a player who acts at once falls asleep while "Vous vous éveillez" burns), that one catches fire now
        /// if it is still whole, and the new one unrolls once it has burnt out: never one cut off by the next.
        /// </summary>
        public void Play(string _text, float _wholeSeconds)
        {
            if (playing)
            {
                pendingText = _text;
                pendingWholeSeconds = _wholeSeconds;
                wholeSeconds = Mathf.Min(wholeSeconds, elapsed);
                return;
            }
            Show(_text, _wholeSeconds);
        }

        private void Show(string _text, float _wholeSeconds)
        {
            label.text = _text;
            wholeSeconds = _wholeSeconds;
            elapsed = 0f;
            burning = false;
            playing = true;
            canvas.enabled = true;
            unrollSound.TryPlayOneShot();
            Apply(ParchmentBannerTimeline.Evaluate(0, wholeSeconds));
        }

        private void Update()
        {
            if (!playing)
            {
                return;
            }
            // Real time: the banner reads the same whatever the game's time scale.
            elapsed += Time.unscaledDeltaTime;
            ParchmentBannerFrame _frame = ParchmentBannerTimeline.Evaluate(elapsed, wholeSeconds);
            if (_frame.Burn > 0 && !burning)
            {
                burning = true;
                burnSound.TryPlayOneShot();
            }
            Apply(_frame);
            if (_frame.Done)
            {
                playing = false;
                canvas.enabled = false;
                if (pendingText != null)
                {
                    string _next = pendingText;
                    pendingText = null;
                    Show(_next, pendingWholeSeconds);
                }
            }
        }

        private void Apply(ParchmentBannerFrame _frame)
        {
            // The banner's rect in the root canvas' space, where UI vertices live (the shader maps the burn on it).
            banner.GetWorldCorners(corners);
            Vector3 _min = canvasRect.InverseTransformPoint(corners[0]);
            Vector3 _max = canvasRect.InverseTransformPoint(corners[2]);
            material.SetVector(BurnRectId, new Vector4(_min.x, _min.y, _max.x - _min.x, _max.y - _min.y));
            material.SetFloat(UnrollId, (float)_frame.Unroll);
            material.SetFloat(BurnId, (float)_frame.Burn);

            float _half = BannerSize.x * 0.5f * (float)_frame.Unroll;
            leftRoller.anchoredPosition = new Vector2(-_half, 0f);
            rightRoller.anchoredPosition = new Vector2(_half, 0f);
            leftRoller.gameObject.SetActive(_frame.RollersVisible);
            rightRoller.gameObject.SetActive(_frame.RollersVisible);
        }

        private void Build()
        {
            material = new Material(burnMaterial) { name = "ParchmentBurn (banner)" };

            var _canvasObject = new GameObject("NightTurnBannerCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _canvasObject.transform.SetParent(transform, false);
            canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.enabled = false;
            canvasRect = (RectTransform)_canvasObject.transform;
            var _scaler = _canvasObject.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = ReferenceResolution;
            _scaler.matchWidthOrHeight = 0.5f;

            banner = NewRect("Banner", canvasRect);
            banner.sizeDelta = BannerSize;
            banner.anchoredPosition = new Vector2(0f, BannerY);

            RectTransform _sheet = NewRect("Parchment", banner);
            Stretch(_sheet);
            var _paper = _sheet.gameObject.AddComponent<RawImage>();
            _paper.texture = parchment;
            float _band = BannerSize.y / BannerSize.x; // the top band of the square texture, unstretched
            _paper.uvRect = new Rect(0f, 1f - _band, 1f, _band);
            _paper.material = material;
            _paper.raycastTarget = false;

            RectTransform _textRect = NewRect("Text", banner);
            Stretch(_textRect);
            _textRect.offsetMax = new Vector2(-TextRightPad, 0f);
            label = _textRect.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.color = textColor;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.material = material;
            label.raycastTarget = false;

            RectTransform _sealRect = NewRect("Seal", banner);
            _sealRect.sizeDelta = new Vector2(SealSize, SealSize);
            _sealRect.anchoredPosition = new Vector2(BannerSize.x * 0.5f - SealInset, 0f);
            _sealRect.localRotation = Quaternion.Euler(0f, 0f, -11.5f);
            var _seal = _sealRect.gameObject.AddComponent<RawImage>();
            _seal.texture = seal;
            _seal.color = sealColor;
            _seal.material = material;
            _seal.raycastTarget = false;

            leftRoller = NewRoller("RollerLeft");
            rightRoller = NewRoller("RollerRight");
        }

        private RectTransform NewRoller(string _name)
        {
            RectTransform _roller = NewRect(_name, banner);
            _roller.sizeDelta = new Vector2(RollerWidth, BannerSize.y + 2f * RollerOverhang);
            var _image = _roller.gameObject.AddComponent<Image>();
            _image.color = rollerColor;
            _image.raycastTarget = false;
            return _roller;
        }

        private static RectTransform NewRect(string _name, Transform _parent)
        {
            var _rect = (RectTransform)new GameObject(_name, typeof(RectTransform)).transform;
            _rect.SetParent(_parent, false);
            return _rect;
        }

        private static void Stretch(RectTransform _rect)
        {
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
