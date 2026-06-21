using UnityEngine;
using UnityEngine.UI;

namespace Reticle
{
    /// <summary>
    /// The screen-center reticle dot (screen-space HUD, NOT world-space). The <see cref="ReticleInteractor"/>
    /// toggles its over-state when the ray is on a targetable card/button. Presentation only; null-tolerant.
    /// </summary>
    public class ReticleHUD : MonoBehaviour
    {
        [SerializeField] private Image _dot;
        [Header("Feel — Poyo-tuned")]
        [SerializeField] private float _idleScale = 1f;
        [SerializeField] private float _overScale = 1.6f;
        [SerializeField] private Color _idleColor = new Color(1f, 1f, 1f, 0.7f);
        [SerializeField] private Color _overColor = new Color(1f, 0.85f, 0.3f, 1f);

        private void Awake() => SetOver(false);

        /// <summary>Show/hide the whole reticle (the interactor calls this with its active state).</summary>
        public void SetVisible(bool _visible)
        {
            if (_dot != null)
            {
                _dot.enabled = _visible;
            }
        }

        /// <summary>Over a targetable element (true) vs idle (false).</summary>
        public void SetOver(bool _over)
        {
            if (_dot == null)
            {
                return;
            }
            _dot.color = _over ? _overColor : _idleColor;
            _dot.rectTransform.localScale = Vector3.one * (_over ? _overScale : _idleScale);
        }
    }
}
