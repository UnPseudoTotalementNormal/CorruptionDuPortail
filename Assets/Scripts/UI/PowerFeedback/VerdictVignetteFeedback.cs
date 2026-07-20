using CorruptionDuPortail.Domain.Powers;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace UI.PowerFeedback
{
    /// <summary>
    /// PLACEHOLDER feedback — a screen-edge vignette that flashes green on a correct power use and red on an
    /// incorrect one, then fades out over about a second. Exists so the detection layer is observable in a
    /// playtest and so the real designed feedback has a working seam to replace.
    ///
    /// DELETE THIS FILE when the designed feedback lands: write the real presenter against
    /// <see cref="IPowerVerdictFeedback"/>, swap it into PowerVerdictFeedbackRouter.feedbackTargets, remove
    /// this component from the scene. Nothing else in the chain changes.
    ///
    /// The vignette texture is generated at runtime rather than authored as a sprite so this placeholder
    /// carries no art asset to clean up later. That is deliberate for throwaway code — the house pattern for
    /// permanent full-screen effects is a material/shader (see UIGlitchGroup).
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class VerdictVignetteFeedback : MonoBehaviour, IPowerVerdictFeedback
    {
        [Header("Which verdicts this presents")]
        [Tooltip("Per-verdict kill switch, so either half can be muted from the inspector without a recompile.")]
        [SerializeField] private bool playOnCorrect = true;
        [SerializeField] private bool playOnIncorrect = true;

        [Header("Placeholder colours — provisional, not a design decision")]
        [SerializeField] private Color correctColor = new(0.30f, 1f, 0.45f, 1f);
        [SerializeField] private Color incorrectColor = new(1f, 0.25f, 0.25f, 1f);

        [Header("Timing")]
        [SerializeField] private float fadeInDuration = 0.22f;
        [SerializeField] private float holdDuration = 0.12f;
        [SerializeField] private float fadeOutDuration = 0.62f;
        [Tooltip("Peak opacity of the border glow. Kept low on purpose — this reads as a hint, not an alarm.")]
        [SerializeField, Range(0f, 1f)] private float peakAlpha = 0.22f;

        [Header("Shape")]
        [Tooltip("Share of the half-screen the border glow bleeds inwards over. 0 = hard edge, 0.5 = full fade to centre.")]
        [SerializeField, Range(0.05f, 0.5f)] private float borderThickness = 0.15f;

        private CanvasGroup _canvasGroup;
        private Image _image;
        private Texture2D _generatedTexture;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            // Purely decorative: never eat a click, never block the board underneath.
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            _image = GetComponent<Image>();
            if (_image)
            {
                _image.raycastTarget = false;
                _image.sprite = BuildVignetteSprite();
                _image.type = Image.Type.Simple;
            }
        }

        public void Play(PowerVerdict verdict)
        {
            if (verdict == PowerVerdict.None || !_canvasGroup) return;
            if (verdict == PowerVerdict.Correct && !playOnCorrect) return;
            if (verdict == PowerVerdict.Incorrect && !playOnIncorrect) return;

            if (_image)
            {
                _image.color = verdict == PowerVerdict.Correct ? correctColor : incorrectColor;
            }

            // DOKill(true) then re-drive, per house style: a second use during the flash restarts it cleanly
            // rather than stacking tweens.
            _canvasGroup.DOKill(true);
            _canvasGroup.alpha = 0f;
            _canvasGroup.DOFade(peakAlpha, fadeInDuration).SetEase(Ease.OutQuint).onComplete += () =>
            {
                _canvasGroup.DOFade(0f, fadeOutDuration).SetEase(Ease.OutQuint).SetDelay(holdDuration);
            };
        }

        private void OnDestroy()
        {
            if (_canvasGroup) _canvasGroup.DOKill();
            if (_generatedTexture) Destroy(_generatedTexture);
        }

        /// <summary>
        /// Builds a square texture whose alpha is opaque at the screen edge and falls off inwards, so a
        /// single stretched Image reads as a border glow. Small (64²) and bilinear-filtered — it is only ever
        /// shown blurred across the full screen, so resolution is irrelevant.
        /// </summary>
        private Sprite BuildVignetteSprite()
        {
            const int SIZE = 64;
            _generatedTexture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var _pixels = new Color32[SIZE * SIZE];
            for (int _y = 0; _y < SIZE; _y++)
            {
                for (int _x = 0; _x < SIZE; _x++)
                {
                    // Normalised distance to the nearest edge, 0 at the border and 0.5 at the centre.
                    float _dx = Mathf.Min(_x, SIZE - 1 - _x) / (float)(SIZE - 1);
                    float _dy = Mathf.Min(_y, SIZE - 1 - _y) / (float)(SIZE - 1);
                    float _edgeDistance = Mathf.Min(_dx, _dy);

                    float _t = Mathf.Clamp01(_edgeDistance / borderThickness);
                    // Smoothstep the falloff so the inner boundary of the glow has no visible banding.
                    float _alpha = 1f - (_t * _t * (3f - 2f * _t));

                    _pixels[_y * SIZE + _x] = new Color32(255, 255, 255, (byte)(_alpha * 255f));
                }
            }

            _generatedTexture.SetPixels32(_pixels);
            _generatedTexture.Apply();

            return Sprite.Create(_generatedTexture, new Rect(0, 0, SIZE, SIZE), new Vector2(0.5f, 0.5f));
        }
    }
}
