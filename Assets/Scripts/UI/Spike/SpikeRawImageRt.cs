using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace UI.Spike
{
    /// <summary>
    /// THROWAWAY SPIKE — the tablet-faithful path (Poyo's approach). Renders a screen-space UI Toolkit panel to
    /// a RenderTexture and shows it on a uGUI <see cref="RawImage"/> placed INSIDE the tablet's existing
    /// world-space ScreenCanvas — reusing the tablet's ScreenMask (stencil clip) + PhoneCamera overlay rig
    /// UNTOUCHED. Only the InfoTable content becomes UITK; everything around it stays uGUI.
    ///
    /// Clicks: the tablet is always free-cursor, so the screen pointer feeds UITK; this maps the screen point to
    /// the RawImage's rect (via the canvas event camera = PhoneCamera) and then to panel pixels through
    /// SetScreenToPanelSpaceFunction. No change to the tablet's GraphicRaycaster.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SpikeRawImageRt : MonoBehaviour
    {
        [SerializeField] private UIDocument _document;
        [Tooltip("RawImage inside the tablet ScreenCanvas that displays the RenderTexture.")]
        [SerializeField] private RawImage _rawImage;
        [Tooltip("Extra RawImages that show the SAME RT (to fake multiple apps in the swipe row — scroll + mask demo).")]
        [SerializeField] private RawImage[] _extraRawImages;
        [Tooltip("The ScreenCanvas worldCamera (PhoneCamera). Needed to map screen clicks onto the world-space RawImage.")]
        [SerializeField] private Camera _canvasCamera;
        [SerializeField] private int _rtWidth = 1024;
        [SerializeField] private int _rtHeight = 683;

        private RenderTexture _rt;
        private RectTransform _rawRect;

        private void OnEnable()
        {
            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
            }

            PanelSettings _ps = _document != null ? _document.panelSettings : null;
            if (_ps == null)
            {
                Debug.LogWarning("[SPIKE] SpikeRawImageRt: no PanelSettings — nothing installed.");
                return;
            }

            _rt = new RenderTexture(_rtWidth, _rtHeight, 24, RenderTextureFormat.ARGB32) { name = "[SPIKE] RT_TabletRaw" };
            _rt.Create();
            _ps.targetTexture = _rt;

            if (_rawImage != null)
            {
                _rawImage.texture = _rt;
                _rawImage.color = Color.white;
                _rawRect = _rawImage.rectTransform;
            }

            if (_extraRawImages != null)
            {
                foreach (RawImage _extra in _extraRawImages)
                {
                    if (_extra != null)
                    {
                        _extra.texture = _rt;
                        _extra.color = Color.white;
                    }
                }
            }

            _ps.SetScreenToPanelSpaceFunction(ScreenToPanel);
            Debug.Log($"[SPIKE] RawImage RT installed {_rtWidth}x{_rtHeight} -> panel + RawImage '{(_rawImage != null ? _rawImage.name : "NULL")}'.");
        }

        private void OnDisable()
        {
            PanelSettings _ps = _document != null ? _document.panelSettings : null;
            if (_ps != null)
            {
                _ps.SetScreenToPanelSpaceFunction(null);
                _ps.targetTexture = null;
            }
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        // Screen pointer -> RawImage local rect -> normalized -> panel pixels (top-left origin, so flip Y).
        private Vector2 ScreenToPanel(Vector2 _screenPos)
        {
            Vector2 _invalid = new Vector2(float.NaN, float.NaN);
            if (_rawRect == null || _rt == null)
            {
                return _invalid;
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rawRect, _screenPos, _canvasCamera, out Vector2 _local))
            {
                return _invalid;
            }
            Rect _r = _rawRect.rect;
            float _u = (_local.x - _r.xMin) / _r.width;
            float _v = (_local.y - _r.yMin) / _r.height;
            if (_u < 0f || _u > 1f || _v < 0f || _v > 1f)
            {
                return _invalid;
            }
            // local rect Y is bottom-up and here already matches panel Y after the RawImage/RT display — no
            // extra flip (an earlier (1 - v) double-flipped, so pointer Y read inverted on the panel).
            return new Vector2(_u * _rt.width, _v * _rt.height);
        }
    }
}
