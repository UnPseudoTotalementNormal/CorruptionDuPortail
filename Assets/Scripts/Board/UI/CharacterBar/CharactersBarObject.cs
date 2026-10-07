#region

using System;
using Characters;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using GameLogic;
using TooltipSystem;
using UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

#endregion

namespace Board.UI.CharacterBar
{
    public class CharactersBarObject : MonoBehaviour
    {
        [HideInInspector] public Character playerCharacter;
        
        [SerializeField] private Image characterImage;

        [Tooltip("Resolves role.rolePortrait -> Sprite (replaces the old Addressables lookup). Wire the PortraitTable asset.")]
        [SerializeField] private PortraitTable portraitTable;

        [SerializeField] private Canvas canvasObject;

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

        [Tooltip("Coloured outline drawn around the portrait while hovered, on top of the zoom: tells the player " +
                 "the portrait is clickable and opens the role card (GD request). Placeholder colour, design-owned.")]
        [SerializeField] private Color hoverOutlineColor = new Color(1f, 0.82f, 0.2f, 1f);

        [SerializeField] private float hoverOutlineWidth = 4f;

        private Outline _hoverOutline;

        [Tooltip("Greyscale material for a role the local player knows is fake (anomalies learn some at game start: " +
                 "GD \"rôle factice en noir et blanc\"). Wire Assets/Shaders/UI_Grayscale.mat.")]
        [SerializeField] private Material knownFakeMaterial;

        private const string KnownFakeTooltipTitle = "Rôle factice";
        private const string KnownFakeTooltipDescription = "Personne ne joue ce rôle dans cette partie.";

        private Material _baseMaterial;
        private bool _baseMaterialCaptured;
        private HoverTooltipComponent _knownFakeTooltip;
        private ICharacterQuery _identitySource;

        /// <summary>
        /// The hover motion's duration, exposed READ-ONLY so children riding that motion
        /// (CharacterBarIconStack) share the single source of truth instead of duplicating the value on
        /// their own serialized field, where the two could silently drift apart.
        /// </summary>
        public float HoverTweenDuration => hoverTweenDuration;

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
            if (characterImage != null)
            {
                _hoverOutline = characterImage.gameObject.AddComponent<Outline>();
                _hoverOutline.effectColor = hoverOutlineColor;
                _hoverOutline.effectDistance = new Vector2(hoverOutlineWidth, -hoverOutlineWidth);
                _hoverOutline.useGraphicAlpha = false;
                _hoverOutline.enabled = false;
            }
            // Story 7.4: CharactersBarObject is instantiated by TWO creators (CharactersBar + NoteRibbon),
            // so a single lane-B push is impractical; it resolves the (non-de-singletonised) revealer from
            // the composition root — behaviour-identical (same scene revealer). Proper injection: Epic 12.
            CompositionRoot.For(NetworkManager.Singleton).GameInfoRevealer.onCharacterInfoRevealedChanged += DoUpdateCharacter;
            // The host possessing another bot changes whose knowledge the bar shows, without any reveal event.
            _identitySource = CompositionRoot.For(NetworkManager.Singleton).CharacterManager;
            if (_identitySource != null) _identitySource.onLocalIdentityChanged += DoUpdateCharacter;
            UpdateCharacter();
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
            if (_hoverOutline != null) _hoverOutline.enabled = true;
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
            if (_hoverOutline != null) _hoverOutline.enabled = false;
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
            UpdateCharacter();
        }

        private bool isSubscribedToCharacter = false;
        private void SubscribeToCharacterEvents()
        {
            if (playerCharacter == null || isSubscribedToCharacter) return;
            playerCharacter.onRoleUpdated += OnCharacterRoleUpdated;

            isSubscribedToCharacter = true;
        }
        private void UnsubscribeFromCharacterEvents()
        {
            if (playerCharacter == null || !isSubscribedToCharacter) return;
            playerCharacter.onRoleUpdated -= OnCharacterRoleUpdated;

            isSubscribedToCharacter = false;
        }
        private void OnDestroy()
        {
            // Teardown hygiene (project rule: NetworkManager.Singleton may be null in OnDestroy during
            // shutdown). At game-end / return-to-menu the CompositionRoot registry can already be cleared
            // (ResetSessionStatics) or the Singleton gone before these bar objects are destroyed, so
            // CompositionRoot.For(...).GameInfoRevealer would NRE. Null-guard the unsubscribe.
            if (NetworkManager.Singleton != null)
            {
                var _revealer = CompositionRoot.For(NetworkManager.Singleton).GameInfoRevealer;
                if (_revealer != null)
                {
                    _revealer.onCharacterInfoRevealedChanged -= DoUpdateCharacter;
                }
            }
            if (_identitySource != null) _identitySource.onLocalIdentityChanged -= DoUpdateCharacter;
            UnsubscribeFromCharacterEvents();
        }
        private void OnCharacterRoleUpdated()
        {
            UpdateCharacter();
        }

        private void UpdateCharacter()
        {
            characterImage.sprite = portraitTable.Get(playerCharacter.GetRole().rolePortrait);
            ShowKnownFake(IsKnownFake());
        }

        // The local player (or the bot the host possesses) was told this role is fake.
        private bool IsKnownFake()
        {
            if (playerCharacter == null || !playerCharacter.isFake || NetworkManager.Singleton == null) return false;
            GameInfoRevealer _revealer = CompositionRoot.For(NetworkManager.Singleton).GameInfoRevealer;
            return _revealer != null &&
                   _revealer.GetCharacterInfo(playerCharacter.ownerClientId.Value).isFakeRevealed > RevealLevel.False;
        }

        // Greyscale portrait + a hover tooltip saying it is fake.
        private void ShowKnownFake(bool _knownFake)
        {
            if (characterImage == null) return;
            if (!_baseMaterialCaptured)
            {
                _baseMaterial = characterImage.material == characterImage.defaultMaterial ? null : characterImage.material;
                _baseMaterialCaptured = true;
            }
            characterImage.material = _knownFake && knownFakeMaterial != null ? knownFakeMaterial : _baseMaterial;

            if (_knownFake && _knownFakeTooltip == null)
            {
                _knownFakeTooltip = gameObject.AddComponent<HoverTooltipComponent>();
                _knownFakeTooltip.SetTooltipTitle(KnownFakeTooltipTitle);
                _knownFakeTooltip.SetTooltipDescription(KnownFakeTooltipDescription);
            }
            if (_knownFakeTooltip != null) _knownFakeTooltip.enabled = _knownFake;
        }
        
        private void DoUpdateCharacter()
        {
            UpdateCharacter();
        }
    }
}