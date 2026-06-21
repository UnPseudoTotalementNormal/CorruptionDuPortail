using UnityEngine;

namespace Presentation
{
    /// <summary>
    /// Assigns a WORLD-space Canvas its event camera so its GraphicRaycaster can be hit by a screen-center
    /// ray (the first-person reticle, or a normal cursor). Reusable on ANY world canvas — without a
    /// worldCamera a world-space GraphicRaycaster can't project the pointer, so UI events never fire.
    /// Falls back to Camera.main and retries until it exists (cards spawn before the camera may be ready).
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class WorldCanvasEventCamera : MonoBehaviour
    {
        [Tooltip("Optional explicit event camera; falls back to Camera.main.")]
        [SerializeField] private Camera _camera;

        private Canvas _canvas;

        private void Awake() => _canvas = GetComponent<Canvas>();
        private void OnEnable() => Apply();

        private void Update()
        {
            // Retry until a camera exists, then stop touching it (no per-frame work once set).
            if (_canvas != null && _canvas.renderMode == RenderMode.WorldSpace && _canvas.worldCamera == null)
            {
                Apply();
            }
        }

        private void Apply()
        {
            Camera _cam = _camera != null ? _camera : Camera.main;
            if (_canvas != null && _cam != null && _canvas.renderMode == RenderMode.WorldSpace)
            {
                _canvas.worldCamera = _cam;
            }
        }
    }
}
