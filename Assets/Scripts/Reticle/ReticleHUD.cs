using UI.CatPaw;
using UnityEngine;
using UnityEngine.UI;

namespace Reticle
{
    /// <summary>
    /// The screen-centre reticle (screen-space HUD, NOT world-space). Maquettes curseur-patte v1, R2: a small round dot
    /// at rest, the cat paw over what can be taken (claws out, pressed on the click, the cross when it refuses); its
    /// toe tip sits on the aimed point like the free mouse's. The <see cref="ReticleInteractor"/> sets the state.
    /// Presentation only; null-tolerant (no art loaded = the old plain dot).
    /// </summary>
    public class ReticleHUD : MonoBehaviour
    {
        [SerializeField] private Image _dot;
        [Header("Feel — Poyo-tuned")]
        [SerializeField] private float _dotSize = 8f;
        [SerializeField] private Color _idleColor = new Color(1f, 1f, 1f, 0.7f);

        private readonly Sprite[] _paws = new Sprite[4];
        private Sprite _dotSprite;
        private PawState _state = PawState.Claws; // anything but Idle, so Awake's SetPaw(Idle) applies

        private void Awake()
        {
            _dotSprite = Resources.Load<Sprite>("CatPaw/reticle_dot");
            for (int _i = 0; _i < _paws.Length; _i++)
            {
                _paws[_i] = Resources.Load<Sprite>("CatPaw/reticle_" + PawArt.Name((PawState)_i));
            }
            SetPaw(PawState.Idle);
        }

        /// <summary>Show/hide the whole reticle (the interactor calls this with its active state).</summary>
        public void SetVisible(bool _visible)
        {
            if (_dot != null)
            {
                _dot.enabled = _visible;
            }
        }

        /// <summary>Idle = the dot; any other state = that paw.</summary>
        public void SetPaw(PawState _paw)
        {
            if (_dot == null || _paw == _state)
            {
                return;
            }
            _state = _paw;
            RectTransform _rt = _dot.rectTransform;
            _rt.localScale = Vector3.one;
            Sprite _sprite = _paw == PawState.Idle ? null : _paws[(int)_paw];
            if (_sprite == null)
            {
                _dot.sprite = _dotSprite;
                _dot.color = _idleColor;
                _rt.pivot = new Vector2(0.5f, 0.5f);
                _rt.sizeDelta = new Vector2(_dotSize, _dotSize);
                return;
            }
            _dot.sprite = _sprite;
            _dot.color = Color.white;
            Sprite _idlePaw = _paws[(int)PawState.Idle] != null ? _paws[(int)PawState.Idle] : _sprite;
            float _h = PawArt.HeightAt1080 * _sprite.rect.height / _idlePaw.rect.height;
            _rt.sizeDelta = new Vector2(_h * _sprite.rect.width / _sprite.rect.height, _h);
            Vector2 _hot = PawArt.Hotspot(_paw);
            _rt.pivot = new Vector2(_hot.x, 1f - _hot.y);
        }
    }
}
