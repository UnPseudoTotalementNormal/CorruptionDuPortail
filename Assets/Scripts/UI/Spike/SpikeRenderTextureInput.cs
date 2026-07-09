using UnityEngine;
using UnityEngine.UIElements;

namespace UI.Spike
{
    /// <summary>
    /// THROWAWAY SPIKE, Path B only (RenderTexture). A screen-space UIDocument renders into a RenderTexture
    /// displayed on a world quad. UITK pointer input is in SCREEN space, so it cannot know the panel lives on a
    /// mesh: this installs a SetScreenToPanelSpaceFunction that raycasts the quad collider, reads the hit UV and
    /// maps it into panel pixel coordinates. This is the documented way to make an RT-backed panel interactive
    /// off a 3D surface. If Path A (native world-space) proves out in play mode, this whole indirection is what
    /// it lets us delete.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SpikeRenderTextureInput : MonoBehaviour
    {
        [SerializeField] private UIDocument _document;
        [Tooltip("The world quad (with a MeshCollider) that displays the RenderTexture.")]
        [SerializeField] private Collider _targetSurface;
        [Tooltip("Renderer of the quad — its material main texture is set to the runtime RenderTexture.")]
        [SerializeField] private Renderer _surfaceRenderer;
        [Tooltip("Camera the player looks through. Falls back to Camera.main.")]
        [SerializeField] private Camera _viewCamera;

        [Header("RenderTexture (created at runtime — no asset needed)")]
        [SerializeField] private int _rtWidth = 1024;
        [SerializeField] private int _rtHeight = 683;

        private RenderTexture _rt;

        private void OnEnable()
        {
            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
            }

            PanelSettings _ps = _document != null ? _document.panelSettings : null;
            if (_ps == null)
            {
                Debug.LogWarning("[SPIKE] SpikeRenderTextureInput: no PanelSettings — RT input not installed.");
                return;
            }

            // Build the RenderTexture in code so no .renderTexture asset is needed. Feed it to BOTH the panel
            // (render target) and the quad's material (display surface) so the screen-space UI shows on the mesh.
            _rt = new RenderTexture(_rtWidth, _rtHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "[SPIKE] RT_Spike (runtime)"
            };
            _rt.Create();
            _ps.targetTexture = _rt;
            if (_surfaceRenderer != null)
            {
                _surfaceRenderer.material.mainTexture = _rt;
                // URP Lit/Unlit sample _BaseMap; set it too so the RT shows regardless of the quad's shader.
                if (_surfaceRenderer.material.HasProperty("_BaseMap"))
                {
                    _surfaceRenderer.material.SetTexture("_BaseMap", _rt);
                }
            }

            _ps.SetScreenToPanelSpaceFunction(ScreenToPanel);
            Debug.Log($"[SPIKE] RT input installed: {_rtWidth}x{_rtHeight} RenderTexture -> panel + quad material (screen -> quad UV -> panel).");
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

        private Vector2 ScreenToPanel(Vector2 _screenPos)
        {
            Vector2 _invalid = new Vector2(float.NaN, float.NaN);
            Camera _cam = _viewCamera != null ? _viewCamera : Camera.main;
            PanelSettings _ps = _document != null ? _document.panelSettings : null;
            if (_cam == null || _targetSurface == null || _ps == null || _ps.targetTexture == null)
            {
                return _invalid;
            }

            Ray _ray = _cam.ScreenPointToRay(_screenPos);
            if (!_targetSurface.Raycast(_ray, out RaycastHit _hit, 100f))
            {
                return _invalid;
            }

            RenderTexture _rt = _ps.targetTexture;
            Vector2 _uv = _hit.textureCoord;
            // Panel space is top-left origin; texture V is bottom-up -> flip Y.
            return new Vector2(_uv.x * _rt.width, (1f - _uv.y) * _rt.height);
        }
    }
}
