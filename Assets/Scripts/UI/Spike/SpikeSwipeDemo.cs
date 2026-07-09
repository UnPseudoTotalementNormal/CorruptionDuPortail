using UnityEngine;
using UnityEngine.InputSystem;

namespace UI.Spike
{
    /// <summary>
    /// DEMO-ONLY swipe for the tablet spike. Left/Right arrow slides the row of app RawImages so you can watch
    /// the tablet's ScreenMask clip apps as they move in/out — the "scroll + mask" check. The REAL tablet uses
    /// SmartphoneController's carousel (anchoredPosition + DOTween); this is a standalone harness so the
    /// extracted tablet swipes without the game running. Slides all apps by a shared offset (no reparenting).
    /// </summary>
    public class SpikeSwipeDemo : MonoBehaviour
    {
        [Tooltip("Apps in LEFT-to-RIGHT visual order. Start index is the centered one.")]
        [SerializeField] private RectTransform[] _apps;
        [SerializeField] private float _step = 1443f;
        [SerializeField] private float _speed = 8f;
        [SerializeField] private int _index = 1;

        private float[] _baseX;
        private float _scroll;
        private float _targetScroll;

        private void Start()
        {
            if (_apps == null)
            {
                return;
            }
            _baseX = new float[_apps.Length];
            for (int _i = 0; _i < _apps.Length; _i++)
            {
                _baseX[_i] = _apps[_i] != null ? _apps[_i].anchoredPosition.x : 0f;
            }
            // Center the start index.
            _targetScroll = -_baseX[Mathf.Clamp(_index, 0, _apps.Length - 1)];
            _scroll = _targetScroll;
            Apply();
        }

        private void Update()
        {
            if (_apps == null || _apps.Length == 0)
            {
                return;
            }

            Keyboard _kb = Keyboard.current;
            if (_kb != null)
            {
                if (_kb.rightArrowKey.wasPressedThisFrame && _index < _apps.Length - 1)
                {
                    _index++;
                    _targetScroll = -_baseX[_index];
                }
                if (_kb.leftArrowKey.wasPressedThisFrame && _index > 0)
                {
                    _index--;
                    _targetScroll = -_baseX[_index];
                }
            }

            _scroll = Mathf.Lerp(_scroll, _targetScroll, Time.deltaTime * _speed);
            Apply();
        }

        private void Apply()
        {
            for (int _i = 0; _i < _apps.Length; _i++)
            {
                if (_apps[_i] != null)
                {
                    Vector2 _p = _apps[_i].anchoredPosition;
                    _p.x = _baseX[_i] + _scroll;
                    _apps[_i].anchoredPosition = _p;
                }
            }
        }
    }
}
