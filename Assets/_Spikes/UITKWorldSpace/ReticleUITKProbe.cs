using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spikes.UITKWorldSpace
{
    /// <summary>
    /// SPIKE (issue #63, Spike A) — THROWAWAY, not shipping code. Validates whether a first-person,
    /// screen-centre RETICLE can hover + confirm elements on a WORLD-SPACE UI Toolkit panel, the way
    /// <c>Reticle.ReticleInteractor</c> already does for world-space uGUI.
    ///
    /// WHY a separate probe instead of reusing the reticle: UITK panels are NOT part of the uGUI
    /// EventSystem / GraphicRaycaster pipeline, so <c>EventSystem.RaycastAll</c> (what ReticleInteractor
    /// uses) can never see them. World-space UITK needs its own pick bridge:
    /// screen-centre ray → panel plane → <see cref="RuntimePanelUtils.CameraTransformWorldToPanel"/> →
    /// <see cref="IPanel.Pick"/>. That conversion is the part reported buggy on 6.3 (Jan 2026): inverted /
    /// unscaled coordinates and pivot-dependent picking.
    ///
    /// DECISIVE TEST = the hover highlight (painted inline, so it does NOT depend on the .uss loading).
    /// If the highlight sits exactly under the reticle as you look around the panel, the coordinate math is
    /// correct → reticle-driven world-space UITK is viable. If it is offset / mirrored / misses, the bug is
    /// reproducing → keep the gameplay UI on uGUI and revisit at 6.7 LTS.
    ///
    /// Runs on the project's current Unity 6.2 (world-space PanelSettings exists since 6.2) — no engine
    /// upgrade needed to get the answer.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ReticleUITKProbe : MonoBehaviour
    {
        [Tooltip("Camera used for the screen-centre ray. Falls back to Camera.main (mirrors WorldCanvasEventCamera).")]
        [SerializeField] private Camera _camera;

        [Tooltip("Optional. If the panel's .uss does not load via <Style src>, drag it here to style the demo. " +
                 "The hover highlight is applied INLINE and does not depend on this.")]
        [SerializeField] private StyleSheet _styleSheet;

        [Tooltip("Colour painted (inline) on whatever element sits under the reticle.")]
        [SerializeField] private Color _hoverTint = new Color(1f, 0.85f, 0.2f, 0.35f);

        [Tooltip("Spam the raw worldHit / panelPos / picked element every frame — turn on to eyeball the " +
                 "CameraTransformWorldToPanel output when picking looks wrong.")]
        [SerializeField] private bool _logEveryFrame;

        private UIDocument _doc;
        private VisualElement _root;
        private VisualElement _hovered;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            _root = _doc != null ? _doc.rootVisualElement : null;
            if (_root != null && _styleSheet != null && !_root.styleSheets.Contains(_styleSheet))
            {
                _root.styleSheets.Add(_styleSheet);
            }
        }

        private void OnDisable() => Unhover();

        private void Update()
        {
            // rootVisualElement can be null for a frame until the UIDocument applies its source asset.
            if (_root == null)
            {
                _root = _doc != null ? _doc.rootVisualElement : null;
                if (_root == null)
                {
                    return;
                }
            }

            IPanel _panel = _root.panel;
            Camera _cam = _camera != null ? _camera : Camera.main;
            if (_panel == null || _cam == null)
            {
                Unhover();
                return;
            }

            // 1) The reticle: a ray through screen centre, exactly like ReticleInteractor (Screen.width/2, height/2).
            Ray _ray = _cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

            // 2) Intersect this GameObject's plane (the world-space panel sits on the UIDocument transform).
            //    Done by hand (not UnityEngine.Plane) to stay side-independent — the panel is hittable from either face.
            Vector3 _n = transform.forward;
            float _denom = Vector3.Dot(_n, _ray.direction);
            if (Mathf.Abs(_denom) < 1e-6f)
            {
                Unhover();
                return;
            }
            float _t = Vector3.Dot(transform.position - _ray.origin, _n) / _denom;
            if (_t <= 0f)
            {
                Unhover();
                return;
            }
            Vector3 _worldHit = _ray.origin + _ray.direction * _t;

            // 3) THE reported-buggy step under test: world point → panel coordinates → pick.
            Vector2 _panelPos = RuntimePanelUtils.CameraTransformWorldToPanel(_panel, _worldHit, _cam);
            VisualElement _picked = _panel.Pick(_panelPos);

            if (_logEveryFrame)
            {
                Debug.Log($"[UITK Spike] worldHit={_worldHit} panelPos={_panelPos} picked={(_picked != null ? _picked.name : "<null>")}");
            }

            Hover(_picked);

            // 4) Confirm → report the Button under the reticle. This is the TARGETING proof; wiring that Button
            //    to real game logic afterwards is a one-liner and not what this spike is de-risking.
            if (ConfirmPressed() && _picked != null)
            {
                Button _btn = _picked as Button ?? _picked.GetFirstAncestorOfType<Button>();
                Debug.Log(_btn != null
                    ? $"[UITK Spike] CONFIRM on Button '{_btn.name}' at panelPos {_panelPos}."
                    : $"[UITK Spike] CONFIRM on '{_picked.name}' (no Button under the reticle).");
            }
        }

        private void Hover(VisualElement _next)
        {
            if (_next == _hovered)
            {
                return;
            }
            Unhover();
            _hovered = _next;
            if (_hovered != null)
            {
                _hovered.style.backgroundColor = new StyleColor(_hoverTint);
            }
        }

        private void Unhover()
        {
            if (_hovered != null)
            {
                // Revert to whatever the .uss / default specifies.
                _hovered.style.backgroundColor = StyleKeyword.Null;
                _hovered = null;
            }
        }

        // Confirm = left mouse, Space, or gamepad south — mirrors ReticleInteractor's out-of-the-box defaults.
        private static bool ConfirmPressed()
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                return true;
            }
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                return true;
            }
            return Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame;
        }
    }
}
