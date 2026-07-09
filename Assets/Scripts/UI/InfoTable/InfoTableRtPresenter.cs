using Smartphone;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace UI.InfoTable
{
    /// <summary>
    /// Renders the screen-space InfoTable UITK panel into a RenderTexture and displays it on a uGUI
    /// <see cref="RawImage"/> inside the tablet's world-space ScreenCanvas — reusing the ScreenMask stencil clip,
    /// the PhoneCamera overlay and the swipe carousel unchanged (Path B from the feasibility spike). Only the
    /// app content becomes UITK. Productionized from <c>SpikeRawImageRt</c>.
    ///
    /// Clicks: the tablet is always free-cursor, so the screen pointer maps to the RawImage rect (via the canvas
    /// event camera = PhoneCamera) and then to panel pixels. Mapping is gated on the owning app being open, so a
    /// swiped-off-center board never eats a phantom click.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class InfoTableRtPresenter : MonoBehaviour
    {
        [SerializeField] private UIDocument _document;

        [Tooltip("RawImage inside the tablet ScreenCanvas that displays the RenderTexture.")]
        [SerializeField] private RawImage _rawImage;

        [Tooltip("The ScreenCanvas worldCamera (PhoneCamera). Maps screen clicks onto the world-space RawImage.")]
        [SerializeField] private Camera _canvasCamera;

        [Tooltip("Optional: pointer mapping is only live while this app is open. Null (harness) = always live.")]
        [SerializeField] private SmartphoneApp _ownerApp;

        [SerializeField] private int _rtWidth = 1440;
        [SerializeField] private int _rtHeight = 912;

        private RenderTexture _rt;
        private RectTransform _rawRect;
        private PanelSettings _sourcePanel;   // the wired PanelSettings asset, captured once
        private PanelSettings _runtimePanel;  // per-instance clone we mutate — never touch the shared asset

        private void OnEnable()
        {
            if (_document == null) _document = GetComponent<UIDocument>();

            // Capture the wired asset once; from then on we render through a per-instance CLONE so we never
            // mutate the shared PanelSettings asset (which would dirty it in-editor and could hijack any other
            // UIDocument — e.g. RoleCard — that references the same asset).
            if (_sourcePanel == null) _sourcePanel = _document != null ? _document.panelSettings : null;
            if (_sourcePanel == null)
            {
                Debug.LogWarning("[InfoTable] RtPresenter: no PanelSettings — nothing installed.");
                if (_rawImage != null) _rawImage.texture = null;
                return;
            }

            _runtimePanel = Instantiate(_sourcePanel);
            _runtimePanel.name = _sourcePanel.name + " (InfoTable runtime)";
            _document.panelSettings = _runtimePanel;

            _rt = new RenderTexture(_rtWidth, _rtHeight, 24, RenderTextureFormat.ARGB32) { name = "RT_InfoTable" };
            _rt.Create();
            _runtimePanel.targetTexture = _rt;

            if (_rawImage != null)
            {
                _rawImage.texture = _rt;
                _rawImage.color = Color.white;
                _rawRect = _rawImage.rectTransform;
            }

            _runtimePanel.SetScreenToPanelSpaceFunction(ScreenToPanel);
        }

        private void OnDisable()
        {
            if (_runtimePanel != null)
            {
                _runtimePanel.SetScreenToPanelSpaceFunction(null);
                _runtimePanel.targetTexture = null;
            }
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
            // Restore the wired asset reference, then destroy our clone (re-enable re-clones from the source).
            if (_document != null && _sourcePanel != null) _document.panelSettings = _sourcePanel;
            if (_runtimePanel != null)
            {
                Destroy(_runtimePanel);
                _runtimePanel = null;
            }
        }

        // Screen pointer -> RawImage local rect -> normalized -> panel pixels. No Y-flip: the local rect Y
        // already matches panel Y after the RawImage/RT display (an extra (1 - v) double-flips — see spike note).
        private Vector2 ScreenToPanel(Vector2 screenPos)
        {
            Vector2 invalid = new Vector2(float.NaN, float.NaN);
            if (_rawRect == null || _rt == null) return invalid;
            if (_ownerApp != null && !_ownerApp.IsOpen) return invalid;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rawRect, screenPos, _canvasCamera, out Vector2 local))
            {
                return invalid;
            }

            Rect r = _rawRect.rect;
            float u = (local.x - r.xMin) / r.width;
            float v = (local.y - r.yMin) / r.height;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return invalid;

            return new Vector2(u * _rt.width, v * _rt.height);
        }
    }
}
