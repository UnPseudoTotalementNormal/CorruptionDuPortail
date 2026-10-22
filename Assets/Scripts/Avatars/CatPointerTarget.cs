using System;
using FX;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Avatars
{
    /// <summary>
    /// T23: a seated cat as something to point at. By day the player votes by clicking a cat and hovering one lights it
    /// and its card (mockups jour-pov v1 H2). A trigger capsule around the body, on the avatar prefab: the seated
    /// reticle's physics ray (<see cref="Reticle.ReticleInteractor"/>) and a free cursor's PhysicsRaycaster both
    /// deliver the pointer events here. Presentation only: what a click does is <see cref="Presentation.DayCatVoting"/>'s
    /// business; it switches the target on (<see cref="SetInteractable"/>) only by day, so nobody points at a cat
    /// hidden at night or walking in the lobby.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CatPointerTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [SerializeField] private PlayerAvatar _avatar;
        [Tooltip("The cat's renderers, outlined while it carries the local player's vote (mockups jour-pov v2 M3).")]
        [SerializeField] private Transform _visualRoot;

        private Collider _collider;

        public PlayerAvatar Avatar => _avatar;

        public event Action<CatPointerTarget> Entered;
        public event Action<CatPointerTarget> Exited;
        public event Action<CatPointerTarget> Clicked;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;
            _collider.enabled = false;
        }

        /// <summary>By day only: the collider answers the reticle and the cursor.</summary>
        public void SetInteractable(bool _interactable)
        {
            if (_collider != null)
            {
                _collider.enabled = _interactable;
            }
        }

        /// <summary>Outlines the whole cat (every part, cosmetics included) while it carries the local player's vote.</summary>
        public void SetOutlined(bool _on)
        {
            if (_visualRoot != null)
            {
                _visualRoot.gameObject.SetOutline(_on ? OutlineState.Selected : OutlineState.None);
            }
        }

        public void OnPointerEnter(PointerEventData _eventData) => Entered?.Invoke(this);

        public void OnPointerExit(PointerEventData _eventData) => Exited?.Invoke(this);

        public void OnPointerClick(PointerEventData _eventData) => Clicked?.Invoke(this);

        private void OnDestroy() => SetOutlined(false);
    }
}
