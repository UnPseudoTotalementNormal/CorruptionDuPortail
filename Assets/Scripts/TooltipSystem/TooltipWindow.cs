using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TooltipSystem
{
    /// <summary>
    /// The parchment slip of a bubble (mockups infobulles v1, A1). A view only: <see cref="TooltipManager"/> fills it,
    /// places it and fades it. Also used, compact, for the keyword cards beside the bubble (K2). Never takes the
    /// pointer: what is under it stays hoverable and clickable.
    /// </summary>
    public class TooltipWindow : MonoBehaviour
    {
        public TMP_Text TitleText;
        public TMP_Text DescriptionText;

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private LayoutElement layoutElement;
        [SerializeField] private VerticalLayoutGroup layoutGroup;

        [Header("Sizes (reference 1920 x 1080)")]
        [SerializeField] private float width = 456f;
        [SerializeField] private float titleSize = 37f;
        [SerializeField] private float descriptionSize = 28f;
        [SerializeField] private float compactWidth = 360f;
        [SerializeField] private float compactTitleSize = 30f;
        [SerializeField] private float compactDescriptionSize = 24f;

        private Tween fadeTween;
        private Tween slideTween;

        public RectTransform Rect => (RectTransform)transform;

        /// <summary>Pixels (reference) still to slide up while it appears.</summary>
        public float Slide { get; private set; }

        public void SetContent(string _title, string _description, bool _compact)
        {
            if (!gameObject.activeSelf)
            {
                // Layout is not computed on an inactive object: wake it up invisible, FadeIn shows it.
                canvasGroup.alpha = 0f;
                gameObject.SetActive(true);
            }
            TitleText.text = _title ?? string.Empty;
            TitleText.gameObject.SetActive(!string.IsNullOrEmpty(_title));
            TitleText.fontSize = _compact ? compactTitleSize : titleSize;

            bool _hasDescription = !string.IsNullOrEmpty(_description);
            DescriptionText.text = _description ?? string.Empty;
            DescriptionText.gameObject.SetActive(_hasDescription);
            DescriptionText.fontSize = _compact ? compactDescriptionSize : descriptionSize;

            float _maxWidth = _compact ? compactWidth : width;
            if (!_hasDescription)
            {
                // A title alone (counters) takes the width it needs, up to the slip's.
                float _padding = layoutGroup != null ? layoutGroup.padding.horizontal : 0f;
                _maxWidth = Mathf.Min(_maxWidth, TitleText.GetPreferredValues(TitleText.text).x + _padding + 1f);
            }
            layoutElement.preferredWidth = _maxWidth;
            LayoutRebuilder.ForceRebuildLayoutImmediate(Rect);
        }

        /// <summary>
        /// Appears, or carries on from where it is: a slip still visible (next trigger, or the power bar rebuilt under
        /// the pointer) only swaps its text instead of blinking.
        /// </summary>
        public void FadeIn(float _duration, float _slide)
        {
            bool _fresh = !gameObject.activeSelf || canvasGroup.alpha <= 0.01f;
            gameObject.SetActive(true);
            fadeTween?.Kill();
            slideTween?.Kill();
            if (_fresh)
            {
                canvasGroup.alpha = 0f;
                Slide = _slide;
            }
            float _remaining = _duration * (1f - canvasGroup.alpha);
            fadeTween = canvasGroup.DOFade(1f, _remaining).SetEase(Ease.OutQuad).SetUpdate(true);
            slideTween = DOTween.To(() => Slide, _value => Slide = _value, 0f, _remaining).SetEase(Ease.OutQuad).SetUpdate(true);
        }

        public void FadeOut(float _duration)
        {
            slideTween?.Kill();
            fadeTween?.Kill();
            if (!gameObject.activeSelf)
            {
                return;
            }
            fadeTween = canvasGroup.DOFade(0f, _duration).SetEase(Ease.InQuad).SetUpdate(true)
                .OnComplete(() => gameObject.SetActive(false));
        }

        private void OnDestroy()
        {
            fadeTween?.Kill();
            slideTween?.Kill();
        }
    }
}
