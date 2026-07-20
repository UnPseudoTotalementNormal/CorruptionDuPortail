using CorruptionDuPortail.Domain.Powers;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace UI.PowerFeedback
{
    /// <summary>
    /// PLACEHOLDER feedback — a centre-screen icon that pops in on a power use: a check mark when the guess
    /// was right, a prohibition sign when it was wrong. Scale pop-in plus fade in/out, roughly a second.
    ///
    /// NO TEXT, NO FONT: the icons are drawn procedurally into a runtime sprite. TextMeshPro's default font
    /// asset carries no emoji glyphs, so a "✅" / "🚫" string would render as tofu. Drawing the shapes
    /// sidesteps the font question entirely and keeps the placeholder asset-free.
    ///
    /// Drop a real sprite into <see cref="correctSpriteOverride"/> / <see cref="incorrectSpriteOverride"/> to
    /// swap in authored art without touching this code. DELETE the whole component once the designed
    /// feedback exists — see <see cref="IPowerVerdictFeedback"/>.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    [RequireComponent(typeof(Image))]
    public class VerdictIconFeedback : MonoBehaviour, IPowerVerdictFeedback
    {
        [Header("Which verdicts this presents")]
        [SerializeField] private bool playOnCorrect = true;
        [SerializeField] private bool playOnIncorrect = true;

        [Header("Authored art (optional — overrides the procedural placeholder)")]
        [SerializeField] private Sprite correctSpriteOverride;
        [SerializeField] private Sprite incorrectSpriteOverride;

        [Header("Placeholder colours — provisional, not a design decision")]
        [SerializeField] private Color correctColor = new(0.42f, 0.92f, 0.52f, 1f);
        [SerializeField] private Color incorrectColor = new(0.95f, 0.35f, 0.35f, 1f);

        [Header("Timing")]
        [SerializeField] private float fadeInDuration = 0.16f;
        [SerializeField] private float holdDuration = 0.36f;
        [SerializeField] private float fadeOutDuration = 0.45f;
        [SerializeField, Range(0f, 1f)] private float peakAlpha = 0.9f;

        [Header("Pop-in")]
        [SerializeField] private float startScale = 0.55f;
        [SerializeField] private float endScale = 1f;
        [Tooltip("Slight drift up-scale while fading out, so the icon dissolves rather than snapping away.")]
        [SerializeField] private float exitScale = 1.12f;

        private CanvasGroup _canvasGroup;
        private Image _image;
        private RectTransform _rect;
        private Sprite _correctSprite;
        private Sprite _incorrectSprite;
        private Texture2D _correctTexture;
        private Texture2D _incorrectTexture;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            _rect = (RectTransform)transform;

            _image = GetComponent<Image>();
            _image.raycastTarget = false;
            _image.type = Image.Type.Simple;
            _image.preserveAspect = true;
        }

        public void Play(PowerVerdict verdict)
        {
            if (verdict == PowerVerdict.None || !_canvasGroup) return;
            if (verdict == PowerVerdict.Correct && !playOnCorrect) return;
            if (verdict == PowerVerdict.Incorrect && !playOnIncorrect) return;

            bool _correct = verdict == PowerVerdict.Correct;
            _image.sprite = _correct ? ResolveCorrectSprite() : ResolveIncorrectSprite();
            _image.color = _correct ? correctColor : incorrectColor;

            // House style: DOKill(true) then re-drive, so a second use mid-animation restarts cleanly.
            _canvasGroup.DOKill(true);
            _rect.DOKill(true);

            _canvasGroup.alpha = 0f;
            _rect.localScale = Vector3.one * startScale;

            // OutBack on the scale gives the "pop" — the only place the project's default OutQuint is not
            // the right curve, because a slight overshoot is what makes it read as a pop rather than a grow.
            _rect.DOScale(endScale, fadeInDuration).SetEase(Ease.OutBack);
            _canvasGroup.DOFade(peakAlpha, fadeInDuration).SetEase(Ease.OutQuint).onComplete += () =>
            {
                _canvasGroup.DOFade(0f, fadeOutDuration).SetEase(Ease.OutQuint).SetDelay(holdDuration);
                _rect.DOScale(exitScale, fadeOutDuration).SetEase(Ease.OutQuint).SetDelay(holdDuration);
            };
        }

        private void OnDestroy()
        {
            if (_canvasGroup) _canvasGroup.DOKill();
            if (_rect) _rect.DOKill();
            if (_correctTexture) Destroy(_correctTexture);
            if (_incorrectTexture) Destroy(_incorrectTexture);
        }

        private Sprite ResolveCorrectSprite()
        {
            if (correctSpriteOverride) return correctSpriteOverride;
            return _correctSprite ??= BuildSprite(DrawCheckMark, ref _correctTexture);
        }

        private Sprite ResolveIncorrectSprite()
        {
            if (incorrectSpriteOverride) return incorrectSpriteOverride;
            return _incorrectSprite ??= BuildSprite(DrawProhibition, ref _incorrectTexture);
        }

        // ---- Procedural icon drawing --------------------------------------------------------------------
        // Both icons are white line-art on transparent; the Image tint supplies the colour. Coverage is
        // computed as a smoothed distance field so the edges are antialiased at any on-screen size.

        private const int ICON_SIZE = 256;
        private const float STROKE = 0.055f;
        private const float EDGE_SOFTNESS = 0.012f;

        private delegate float CoverageAt(float x, float y);

        private static Sprite BuildSprite(CoverageAt _coverage, ref Texture2D _texture)
        {
            _texture = new Texture2D(ICON_SIZE, ICON_SIZE, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var _pixels = new Color32[ICON_SIZE * ICON_SIZE];
            for (int _y = 0; _y < ICON_SIZE; _y++)
            {
                for (int _x = 0; _x < ICON_SIZE; _x++)
                {
                    float _u = _x / (float)(ICON_SIZE - 1);
                    float _v = _y / (float)(ICON_SIZE - 1);
                    float _alpha = Mathf.Clamp01(_coverage(_u, _v));
                    _pixels[_y * ICON_SIZE + _x] = new Color32(255, 255, 255, (byte)(_alpha * 255f));
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply();
            return Sprite.Create(_texture, new Rect(0, 0, ICON_SIZE, ICON_SIZE), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Two-segment tick: a short down-stroke into a long up-stroke.</summary>
        private static float DrawCheckMark(float _x, float _y)
        {
            float _d = Mathf.Min(
                DistanceToSegment(_x, _y, 0.24f, 0.52f, 0.43f, 0.32f),
                DistanceToSegment(_x, _y, 0.43f, 0.32f, 0.78f, 0.70f));
            return Smooth(_d, STROKE);
        }

        /// <summary>Ring plus a diagonal bar clipped to the ring — the no-entry sign.</summary>
        private static float DrawProhibition(float _x, float _y)
        {
            const float RADIUS = 0.36f;

            float _dx = _x - 0.5f;
            float _dy = _y - 0.5f;
            float _radial = Mathf.Sqrt(_dx * _dx + _dy * _dy);

            float _ring = Smooth(Mathf.Abs(_radial - RADIUS), STROKE);

            // Bar rotated 45°, then clipped to the disc so it never pokes out of the ring.
            const float SQRT_HALF = 0.70710678f;
            float _rotated = Mathf.Abs((_dx + _dy) * SQRT_HALF);
            float _bar = Smooth(_rotated, STROKE) * Smooth(Mathf.Max(0f, _radial - RADIUS), STROKE * 0.5f);

            return Mathf.Max(_ring, _bar);
        }

        /// <summary>Coverage for a stroke of half-width <paramref name="_halfWidth"/> at distance <paramref name="_distance"/>.</summary>
        private static float Smooth(float _distance, float _halfWidth) =>
            1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_halfWidth - EDGE_SOFTNESS, _halfWidth + EDGE_SOFTNESS, _distance));

        private static float DistanceToSegment(float _px, float _py, float _ax, float _ay, float _bx, float _by)
        {
            float _abx = _bx - _ax;
            float _aby = _by - _ay;
            float _lengthSq = _abx * _abx + _aby * _aby;
            float _t = _lengthSq <= Mathf.Epsilon
                ? 0f
                : Mathf.Clamp01(((_px - _ax) * _abx + (_py - _ay) * _aby) / _lengthSq);
            float _dx = _px - (_ax + _t * _abx);
            float _dy = _py - (_ay + _t * _aby);
            return Mathf.Sqrt(_dx * _dx + _dy * _dy);
        }
    }
}
