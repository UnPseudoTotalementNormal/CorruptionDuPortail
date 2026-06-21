using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Reticle
{
    /// <summary>
    /// First-person Vote targeting (PR1). The seated embodied camera has no OS cursor, so this casts a ray
    /// from the camera CENTER each frame and delivers <c>OnPointerEnter/Exit/Click</c> to whatever world
    /// object the player is looking at — reusing the EXISTING <c>IPointer*</c> handlers on the cards and the
    /// world vote/skip <c>CustomButton</c>s via <see cref="ExecuteEvents"/> (no changes to those types).
    ///
    /// Arbiter-driven like the seating presenter / visibility controller: active ONLY while Embodied
    /// (<see cref="SetActive"/>). Targeting hysteresis lives in the pure <see cref="ReticleHover"/>.
    /// Presentation only — it just synthesizes pointer events a GraphicRaycaster would otherwise produce.
    /// (PR2 adds the dramatic hover lift+turn and the static anti-jitter envelope.)
    /// </summary>
    public class ReticleInteractor : MonoBehaviour
    {
        [Tooltip("The rendering (Cinemachine brain) camera the player looks through. Falls back to Camera.main.")]
        [SerializeField] private Camera _camera;
        [SerializeField] private ReticleHUD _hud;

        [Header("Raycast")]
        [SerializeField] private LayerMask _targetMask = ~0;
        [SerializeField] private float _maxDistance = 50f;

        [Header("Hysteresis (Poyo-tuned)")]
        [Tooltip("Seconds the ray must stay OFF the current target before it un-hovers (anti-flicker).")]
        [SerializeField] private float _exitDwell = 0.12f;
        [Tooltip("Seconds a NEW target must persist before the reticle switches to it (anti-strobe).")]
        [SerializeField] private float _switchDebounce = 0.06f;

        [Tooltip("Confirm action (e.g. Fire / left-click / gamepad south) — Poyo wires the binding. Null-tolerant.")]
        [SerializeField] private InputActionReference _confirmAction;

        private bool _active;
        private ReticleHover _hover;
        private GameObject _currentHandler;
        private PointerEventData _pointerData;

        private void Awake()
        {
            _hover = new ReticleHover(_exitDwell, _switchDebounce);
            if (_camera == null)
            {
                _camera = Camera.main;
            }
            _hud?.SetVisible(false);
        }

        /// <summary>Arbiter contract (mirror AvatarSeatingPresenter/AvatarVisibilityController).</summary>
        public void SetActive(bool _isActive)
        {
            if (_isActive == _active)
            {
                return;
            }
            _active = _isActive;
            _hud?.SetVisible(_active);

            if (_active)
            {
                if (_confirmAction != null && _confirmAction.action != null)
                {
                    _confirmAction.action.Enable();
                }
            }
            else
            {
                // Leaving Vote: release any hovered target cleanly so it doesn't stay stuck-hovered.
                int _exited = _hover.Reset();
                if (_exited == _currentHandler?.GetInstanceID())
                {
                    DispatchExit(_currentHandler);
                }
                _currentHandler = null;
                _hud?.SetOver(false);
                if (_confirmAction != null && _confirmAction.action != null)
                {
                    _confirmAction.action.Disable();
                }
            }
        }

        private void Update()
        {
            if (!_active || _camera == null)
            {
                return;
            }

            GameObject _hit = ResolveHandlerUnderReticle();
            int _hitId = _hit != null ? _hit.GetInstanceID() : ReticleHover.None;

            ReticleHoverResult _r = _hover.Tick(_hitId, Time.deltaTime);
            if (_r.Exited != ReticleHover.None)
            {
                DispatchExit(_currentHandler);
                _currentHandler = null;
                _hud?.SetOver(false);
            }
            if (_r.Entered != ReticleHover.None)
            {
                _currentHandler = _hit;
                DispatchEnter(_currentHandler);
                _hud?.SetOver(true);
            }

            // Confirm → click the current target (the on-card vote button, a card, or the world skip button).
            if (_currentHandler != null && ConfirmPressed())
            {
                DispatchClick(_currentHandler);
            }
        }

        // Center-screen ray → the GameObject that actually HANDLES pointer events (walks up to the Card root
        // or the CustomButton), or null. ExecuteEvents.GetEventHandler does the parent walk.
        private GameObject ResolveHandlerUnderReticle()
        {
            Transform _t = _camera.transform;
            if (!Physics.Raycast(_t.position, _t.forward, out RaycastHit _rayHit, _maxDistance, _targetMask,
                    QueryTriggerInteraction.Collide))
            {
                return null;
            }
            return ExecuteEvents.GetEventHandler<IPointerEnterHandler>(_rayHit.collider.gameObject);
        }

        private bool ConfirmPressed() =>
            _confirmAction != null && _confirmAction.action != null && _confirmAction.action.WasPressedThisFrame();

        private PointerEventData PointerData()
        {
            _pointerData ??= new PointerEventData(EventSystem.current);
            _pointerData.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            return _pointerData;
        }

        private void DispatchEnter(GameObject _go) =>
            ExecuteEvents.Execute(_go, PointerData(), ExecuteEvents.pointerEnterHandler);

        private void DispatchExit(GameObject _go)
        {
            if (_go != null)
            {
                ExecuteEvents.Execute(_go, PointerData(), ExecuteEvents.pointerExitHandler);
            }
        }

        private void DispatchClick(GameObject _go) =>
            ExecuteEvents.Execute(_go, PointerData(), ExecuteEvents.pointerClickHandler);
    }
}
