#region

using System;
using Characters;
using Characters.Powers;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using GameLogic;
using TooltipSystem;
using UI;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace Board.UI.CharacterBar
{
    public class CharactersBarObject : MonoBehaviour
    {
        [HideInInspector] public Character playerCharacter;
        
        [SerializeField] private Image characterImage;

        [SerializeField] private Canvas canvasObject;
        
        [SerializeField] private Image corruptedOverlayImage;
        
        [SerializeField] private HoverTooltipComponent hoverTooltipComponent;
        
        private CustomButton customButton;

        [Tooltip("Visual that is moved toward the camera on hover (e.g. the portrait child). Kept separate " +
                 "from the root so the root's static raycast target never moves — otherwise the moving " +
                 "collider would leave the cursor and spam hover/unhover at the edges.")]
        [SerializeField] private Transform hoverVisual;

        [Tooltip("World-space distance the visual moves straight toward the camera on hover. Small but " +
                 "noticeable — like lifting the card off the board toward the viewer.")]
        [SerializeField] private float hoverCameraZoomDistance = 1.5f;

        [Tooltip("When hovered, rotate the visual to face the camera head-on (as if unhooked from the " +
                 "tilted board and held up to look at it).")]
        [SerializeField] private bool hoverFaceCamera = true;

        [SerializeField] private float hoverTweenDuration = 0.35f;

        private Transform HoverVisual => hoverVisual != null ? hoverVisual : transform;

        private Camera _mainCamera;
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private bool _hasRestPose;

        public event Action<Character> onCharacterBarObjectClicked;
        public event Action<Character> onCharacterBarObjectHovered;
        public event Action<Character> onCharacterBarObjectUnhovered;

        private void Start()
        {
            if (!TryGetComponent(out customButton))
            {
                customButton = gameObject.AddComponent<CustomButton>();
            }
            customButton.onButtonClicked += () =>
            {
                onCharacterBarObjectClicked?.Invoke(playerCharacter);
            };
            customButton.onButtonHovered += OnButtonHovered;
            customButton.onButtonUnhovered += OnButtonUnhovered;
            GameManager.instance.gameInfoRevealer.onCharacterInfoRevealedChanged += DoUpdateCharacter;
        }

        private void OnButtonHovered()
        {
            Transform _visual = HoverVisual;
            EnsureCamera();
            if (!_hasRestPose)
            {
                _restPosition = _visual.position;
                _restRotation = _visual.rotation;
                _hasRestPose = true;
            }

            _visual.DOKill();
            _visual.DOMove(_restPosition + GetCameraZoomOffset(), hoverTweenDuration).SetEase(Ease.OutQuint);
            if (hoverFaceCamera && _mainCamera != null)
            {
                _visual.DORotateQuaternion(GetFaceCameraRotation(), hoverTweenDuration).SetEase(Ease.OutQuint);
            }
            canvasObject.sortingOrder += 1;
            onCharacterBarObjectHovered?.Invoke(playerCharacter);
        }

        private void OnButtonUnhovered()
        {
            Transform _visual = HoverVisual;
            _visual.DOKill();
            if (_hasRestPose)
            {
                _visual.DOMove(_restPosition, hoverTweenDuration).SetEase(Ease.OutQuint);
                _visual.DORotateQuaternion(_restRotation, hoverTweenDuration).SetEase(Ease.OutQuint);
            }
            canvasObject.sortingOrder -= 1;
            onCharacterBarObjectUnhovered?.Invoke(playerCharacter);
        }

        private void EnsureCamera()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
            }
        }

        // Small straight move toward the camera: the card lifts off the board toward the viewer.
        private Vector3 GetCameraZoomOffset()
        {
            EnsureCamera();
            if (_mainCamera == null)
            {
                return Vector3.zero;
            }

            Transform _visual = HoverVisual;
            return (_mainCamera.transform.position - _visual.position).normalized * hoverCameraZoomDistance;
        }

        // Rotation that turns the visual to face the camera head-on. Forward points away from the camera
        // (the world-space UI front is visible from the opposite side), and the camera's up keeps text upright.
        private Quaternion GetFaceCameraRotation()
        {
            Transform _visual = HoverVisual;
            Vector3 _awayFromCamera = _visual.position - _mainCamera.transform.position;
            return Quaternion.LookRotation(_awayFromCamera, _mainCamera.transform.up);
        }
        
        public void SetCharacter(Character _character)
        {
            UnsubscribeFromCharacterEvents();
            playerCharacter = _character;
            SubscribeToCharacterEvents();
            _ = UpdateCharacter();
        }

        private bool isSubscribedToCharacter = false;
        private void SubscribeToCharacterEvents()
        {
            if (playerCharacter == null || isSubscribedToCharacter) return;
            playerCharacter.onRoleUpdated += OnCharacterRoleUpdated;
            playerCharacter.isCorrupted.OnValueChanged += OnCorruptedChanged;
            
            isSubscribedToCharacter = true;
        }
        private void UnsubscribeFromCharacterEvents()
        {
            if (playerCharacter == null || !isSubscribedToCharacter) return;
            playerCharacter.onRoleUpdated -= OnCharacterRoleUpdated;
            playerCharacter.isCorrupted.OnValueChanged -= OnCorruptedChanged;
            
            isSubscribedToCharacter = false;
        }
        private void OnDestroy()
        {
            GameManager.instance.gameInfoRevealer.onCharacterInfoRevealedChanged -= DoUpdateCharacter;
            UnsubscribeFromCharacterEvents();
        }
        private void OnCharacterRoleUpdated()
        {
            _ = UpdateCharacter();
        }
        private void OnCorruptedChanged(bool previous, bool current)
        {
            _ = UpdateCharacter();
        }

        private async UniTaskVoid UpdateCharacter()
        {
            RevealLevel _forceCorruptOnRoleRevealed = GameManager.instance.gameInfoRevealer.GetCharacterInfo(playerCharacter.ownerClientId.Value).forceCorruptOnRoleRevealed;
            bool _isCorrupted = playerCharacter.isCorrupted.Value && _forceCorruptOnRoleRevealed > RevealLevel.False;
            if (corruptedOverlayImage)
            {
                corruptedOverlayImage.DOFade(_isCorrupted ? 0.65f : 0, 0.35f);
            }

            if (hoverTooltipComponent)
            {
                hoverTooltipComponent.SetTooltipTitle(playerCharacter.role.roleName.ToString());
                string _description = "Pouvoirs:";
                foreach (Power _power in playerCharacter.role.powers)
                {
                    _description += $"\n- <link=power_{_power.ownerClientId.Value}_{_power.NetworkObjectId}>{_power.powerName}</link>";
                }
                hoverTooltipComponent.SetTooltipDescription(_description);
            }
            
            var _rolePortrait = await playerCharacter.GetRole().GetRolePortrait();
            characterImage.sprite = _rolePortrait;
        }
        
        private void DoUpdateCharacter()
        {
            _ = UpdateCharacter();
        }
    }
}