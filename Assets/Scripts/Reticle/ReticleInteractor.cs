using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Reticle
{
    /// <summary>
    /// First-person Vote targeting (PR1). The seated embodied camera has no OS cursor, so this drives a
    /// SCREEN-CENTRE uGUI raycast (<see cref="EventSystem"/>.RaycastAll) each frame and delivers
    /// <c>OnPointerEnter/Exit/Click</c> to whatever world
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
        [SerializeField] private ReticleHUD _hud;

        [Header("Hysteresis (Poyo-tuned)")]
        [Tooltip("Seconds the ray must stay OFF the current target before it un-hovers (anti-flicker).")]
        [SerializeField] private float _exitDwell = 0.12f;
        [Tooltip("Seconds a NEW target must persist before the reticle switches to it (anti-strobe).")]
        [SerializeField] private float _switchDebounce = 0.06f;

        [Tooltip("Confirm action (e.g. Fire / left-click / gamepad south) — Poyo wires the binding. Null-tolerant.")]
        [SerializeField] private InputActionReference _confirmAction;

        [Header("World 3D (physics) targeting")]
        [Tooltip("Layers the screen-centre physics ray hits for 3D interactables (powers, board 3D buttons).")]
        [SerializeField] private LayerMask _worldMask = ~0;
        [Tooltip("Max physics ray distance for 3D targeting.")]
        [SerializeField] private float _worldMaxDistance = 50f;
        [Tooltip("Camera the screen-centre physics ray is cast from. Falls back to Camera.main when null.")]
        [SerializeField] private Camera _worldCamera;

        private bool _active;
        // TWO independent hover tracks: the BODY (the card, hit by physics — the static collider, anti-jitter)
        // and the world UI (buttons, hit by the GraphicRaycaster). They are separate so aiming a card's own
        // vote button (a child UI) does NOT exit the card body underneath it — the card stays raised while the
        // button highlights. The physics ray passes through the thin UI to the card collider behind, so the
        // body stays hovered the whole time the reticle is anywhere on the card.
        private ReticleHover _bodyHover;
        private ReticleHover _uiHover;
        // Third track: 3D world interactables (powers, board 3D buttons) resolved by a screen-centre physics
        // ray. Separate from the two uGUI tracks so a 3D object and a uGUI control never fight over one track.
        private ReticleHover _worldHover;
        private GameObject _bodyHandler;
        private GameObject _uiHandler;
        private GameObject _worldHandler;
        private PointerEventData _pointerData;
        private readonly List<RaycastResult> _uiResults = new();

        private void Awake()
        {
            _bodyHover = new ReticleHover(_exitDwell, _switchDebounce);
            _uiHover = new ReticleHover(_exitDwell, _switchDebounce);
            _worldHover = new ReticleHover(_exitDwell, _switchDebounce);
            _hud?.SetVisible(false);
        }

        // Teardown safety: if this is disabled or its scene is unloaded while still active (e.g. scene unload
        // mid-Vote — AvatarCameraArbiter anticipates the same for the cursor), no camera-mode transition fires,
        // so the confirm action would stay Enable()d on the shared InputActionAsset and the hovered target would
        // keep its OnPointerEnter with no matching exit. Funnel through SetActive(false) to release both cleanly.
        private void OnDisable()
        {
            if (_active)
            {
                SetActive(false);
            }
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
                // Leaving Vote: release all tracks cleanly so nothing stays stuck-hovered.
                ResetTrack(_bodyHover, ref _bodyHandler);
                ResetTrack(_uiHover, ref _uiHandler);
                ResetTrack(_worldHover, ref _worldHandler);
                _hud?.SetOver(false);
                if (_confirmAction != null && _confirmAction.action != null)
                {
                    _confirmAction.action.Disable();
                }
            }
        }

        private void Update()
        {
            if (!_active)
            {
                return;
            }

            // Resolve both targets from the SAME stable uGUI raycast (works at any card tilt; the physics
            // collider was unreliable because it rides the compositor and tilts away under the reticle).
            ResolveTargets(out GameObject _body, out GameObject _ui);
            GameObject _world = ResolveWorld();
            UpdateTrack(_bodyHover, ref _bodyHandler, _body);
            UpdateTrack(_uiHover, ref _uiHandler, _ui);
            UpdateTrack(_worldHover, ref _worldHandler, _world);

            _hud?.SetOver(_bodyHandler != null || _uiHandler != null || _worldHandler != null);

            // Confirm → click priority: a uGUI control (vote/skip button) first, then a 3D interactable
            // (power / board 3D button), then the body (card) underneath. Only with a locked cursor: otherwise the
            // InputSystemUIInputModule clicks what is under the free pointer itself (same button → clicked twice,
            // or the reticle clicking the screen centre while the player clicks elsewhere).
            if (ConfirmPressed() && ShouldDispatchClick(Cursor.lockState == CursorLockMode.Locked))
            {
                GameObject _clickTarget = _uiHandler != null ? _uiHandler
                    : (_worldHandler != null ? _worldHandler : _bodyHandler);
                if (_clickTarget != null)
                {
                    DispatchClick(_clickTarget);
                }
            }
        }

        // Advance one hover track: dispatch enter/exit as the per-track target changes (hysteresis in ReticleHover).
        private void UpdateTrack(ReticleHover _track, ref GameObject _current, GameObject _hit)
        {
            int _id = _hit != null ? _hit.GetEntityId().GetHashCode() : ReticleHover.None;
            ReticleHoverResult _r = _track.Tick(_id, Time.deltaTime);
            if (_r.Exited != ReticleHover.None)
            {
                DispatchExit(_current);
                _current = null;
            }
            if (_r.Entered != ReticleHover.None)
            {
                _current = _hit;
                DispatchEnter(_current);
            }
        }

        private void ResetTrack(ReticleHover _track, ref GameObject _current)
        {
            _track.Reset();
            if (_current != null)
            {
                DispatchExit(_current);
                _current = null;
            }
        }

        // One screen-center uGUI raycast resolves BOTH tracks from the stack of hits under the reticle:
        //  • _ui  = the TOPMOST control (the vote/skip button) — gets highlight + click;
        //  • _body = the handler BEHIND it (the card, whose face graphic is hit even behind its own button) —
        //            gets the hover/raise, and STAYS hovered while the reticle is on the card OR its button.
        // When only one handler is under the reticle (the card body, no front control), it IS the body and
        // there is no separate UI target. This is stable at any card tilt (GraphicRaycaster projects screen→
        // canvas), unlike the physics collider which rides the compositor and tilts out from under the ray.
        private void ResolveTargets(out GameObject _body, out GameObject _ui)
        {
            _body = null;
            _ui = null;

            EventSystem _es = EventSystem.current;
            if (_es == null)
            {
                return;
            }

            _uiResults.Clear();
            _es.RaycastAll(PointerData(), _uiResults);

            GameObject _top = null;
            GameObject _behind = null;
            for (int _i = 0; _i < _uiResults.Count; _i++)
            {
                GameObject _handler = ExecuteEvents.GetEventHandler<IPointerEnterHandler>(_uiResults[_i].gameObject);
                if (_handler == null)
                {
                    continue;
                }
                if (_top == null)
                {
                    _top = _handler;
                }
                else if (_handler != _top)
                {
                    _behind = _handler;
                    break;
                }
            }

            if (_behind != null)
            {
                _body = _behind; // the card behind the front control
                _ui = _top;      // the control (button) on top
            }
            else
            {
                _body = _top;    // a single handler — the card body itself (or a standalone button)
            }
        }

        // Screen-centre physics ray → the IPointer handler of the 3D interactable under the reticle (its own
        // collider or a parent that implements the handler). Gated to a LOCKED cursor (the seated embodied
        // look mode): the free-cursor mode reaches the very same colliders through their OnMouseDown, so
        // restricting this to locked-cursor is what stops a 3D object being clicked twice.
        private GameObject ResolveWorld()
        {
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return null;
            }
            // Don't let the reticle reach a 3D interactable THROUGH uGUI: if the screen-centre RaycastAll that
            // ResolveTargets already ran this frame hit any blocking graphic, suppress the world track. Scoped
            // to the world track only — the two uGUI tracks (_ui/_body) keep dispatching, so a vote/skip button
            // under the reticle still gets its events. The reticle HUD dot is raycastTarget:false, so it never
            // appears in _uiResults; hence a raw count is enough (blocking panels render ScreenSpace-Overlay).
            if (ShouldSuppressWorld(_uiResults.Count))
            {
                return null;
            }
            Camera _cam = _worldCamera != null ? _worldCamera : Camera.main;
            if (_cam == null)
            {
                return null;
            }
            Ray _ray = _cam.ScreenPointToRay(PointerData().position);
            if (!Physics.Raycast(_ray, out RaycastHit _hit, _worldMaxDistance, _worldMask, QueryTriggerInteraction.Collide))
            {
                return null;
            }
            return ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hit.collider.gameObject);
        }

        /// <summary>
        /// Pure decision (unit-tested): the world-3D track is suppressed whenever a blocking uGUI graphic is
        /// under the reticle. A raw count is sufficient because the reticle's own HUD dot is not a raycast
        /// target and the blocking panels are ScreenSpace-Overlay (always in front of world geometry).
        /// </summary>
        public static bool ShouldSuppressWorld(int _uiHitCount) => _uiHitCount > 0;

        /// <summary>
        /// Pure decision (unit-tested): the reticle clicks only while the cursor is really locked. The UI input
        /// module ignores a locked pointer (CursorLockBehavior OutsideScreen), so the reticle is then the only one
        /// to click; with a free cursor (platform unlock, unfocused window, autoplay) the module clicks under the
        /// pointer, and a reticle click on top delivered every click twice (seen as duplicate votes).
        /// </summary>
        public static bool ShouldDispatchClick(bool _cursorLocked) => _cursorLocked;

        // Confirm = the wired action if any, else a sensible default so clicking works out of the box: left
        // mouse button or gamepad south (the cursor is locked but the buttons still register).
        private bool ConfirmPressed()
        {
            if (_confirmAction != null && _confirmAction.action != null)
            {
                return _confirmAction.action.WasPressedThisFrame();
            }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                return true;
            }
            return Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame;
        }

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
