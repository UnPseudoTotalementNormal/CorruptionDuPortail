#region

using DG.Tweening;
using Extensions;
using FMODUnity;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;

#endregion

namespace Board.UI
{
    /// <summary>
    /// Turns a 3D world model into a clickable board button. The model carries the collider; the actual
    /// game behaviour (opening a panel, gating, sounds, DI) stays on an invisible, logic-only
    /// <see cref="CustomButton"/> that this simply forwards to — so the existing UnityEvent wiring, the
    /// AnonymeMessageButton gate and the DiSeam guard are untouched.
    ///
    /// Interaction is driven purely through the EventSystem <c>IPointer*</c> handlers, from both input modes:
    ///  • free cursor (board overview) → the scene's PhysicsRaycaster + InputSystemUIInputModule,
    ///  • seated embodied (locked cursor) → <see cref="Reticle.ReticleInteractor"/>'s screen-centre physics ray.
    /// Neither fires at the same time (the module goes silent while the cursor is locked), and the module's
    /// topmost-first picking already blocks interaction through an open uGUI panel — so no legacy OnMouse*
    /// handlers and no manual "is over UI" gate are needed (they double-fired and, because a PhysicsRaycaster
    /// makes IsPointerOverGameObject() true over the model itself, spuriously killed the hover).
    ///
    /// Presentation mirrors PowerBarObject3D: DOTween hover scale, click punch, FMOD one-shots and an
    /// outline-layer swap on hover.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Board3DButton : MonoBehaviour,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("Invisible logic-only CustomButton this forwards clicks to (holds the TryOpenPanel wiring).")]
        [SerializeField] private CustomButton targetButton;
        [Tooltip("Model transform to animate. Defaults to this GameObject's transform when null.")]
        [SerializeField] private Transform visualTransform;

        [Header("Feedback")]
        [SerializeField] private float hoverScale = 1.1f;
        [SerializeField] private float hoverTweenDuration = 0.15f;
        [SerializeField] private float clickPunchIntensity = 0.15f;
        [SerializeField] private float clickPunchDuration = 0.25f;
        [Tooltip("Optional. Leave empty to let the forwarded CustomButton play its own click sound (avoids doubling).")]
        [SerializeField] private EventReference clickSound;
        [SerializeField] private EventReference hoverSound;

        [Header("Outline")]
        [SerializeField] private bool useOutline = true;
        [SerializeField] private string hoverLayerName = "Outline_Hover";

        private Vector3 _baseScale;
        private int _defaultLayer;
        private int _hoverLayer = -1;
        private bool _hovering;

        // Mirrors the CustomButton's own enabled gate (e.g. AnonymeMessageButton disables itself when the
        // player may not send a message this turn) so the 3D model refuses hover/click in exactly the same cases.
        private bool Interactable => targetButton == null || targetButton.isActiveAndEnabled;

        private void Awake()
        {
            if (visualTransform == null)
            {
                visualTransform = transform;
            }
            _baseScale = visualTransform.localScale;
            _defaultLayer = visualTransform.gameObject.layer;
            if (useOutline)
            {
                _hoverLayer = LayerMask.NameToLayer(hoverLayerName);
            }
        }

        public void OnPointerClick(PointerEventData _eventData)
        {
            Activate();
        }

        public void OnPointerEnter(PointerEventData _eventData)
        {
            Enter();
        }

        public void OnPointerExit(PointerEventData _eventData)
        {
            Exit();
        }

        private void Activate()
        {
            if (!Interactable)
            {
                return;
            }
            clickSound.TryPlayOneShot();
            PunchClick();
            if (targetButton != null)
            {
                // Reuse the exact CustomButton path (punch + click sound + onButtonClickedUnityEvent → TryOpenPanel).
                targetButton.OnPointerClick(new PointerEventData(EventSystem.current));
            }
        }

        private void Enter()
        {
            if (!Interactable || _hovering)
            {
                return;
            }
            _hovering = true;
            hoverSound.TryPlayOneShot();
            if (visualTransform == null)
            {
                return;
            }
            visualTransform.DOKill();
            visualTransform.DOScale(_baseScale * hoverScale, hoverTweenDuration).SetEase(Ease.OutQuint);
            if (useOutline && _hoverLayer >= 0)
            {
                visualTransform.gameObject.SetLayerRecursively(_hoverLayer);
            }
        }

        private void Exit()
        {
            if (!_hovering)
            {
                return;
            }
            _hovering = false;
            if (visualTransform == null)
            {
                return;
            }
            visualTransform.DOKill();
            visualTransform.DOScale(_baseScale, hoverTweenDuration).SetEase(Ease.OutQuint);
            if (useOutline && _hoverLayer >= 0)
            {
                visualTransform.gameObject.SetLayerRecursively(_defaultLayer);
            }
        }

        private void PunchClick()
        {
            if (visualTransform == null)
            {
                return;
            }
            visualTransform.DOPunchScale(_baseScale * clickPunchIntensity, clickPunchDuration, 1, 0.2f);
        }

        private void OnDisable()
        {
            if (_hovering)
            {
                Exit();
            }
        }
    }
}
