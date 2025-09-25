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
        
        private Vector3 originalScale;
        public Vector3 hoverScale = new Vector3(1.2f, 1.2f, 1.2f);
        
        public event Action<Character> onCharacterBarObjectClicked;
        public event Action<Character> onCharacterBarObjectHovered;
        public event Action<Character> onCharacterBarObjectUnhovered;

        private void Start()
        {
            originalScale = transform.localScale;
            
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

        }

        private void OnButtonHovered()
        {
            transform.DOKill();
            transform.DOScale(hoverScale, 0.35f).SetEase(Ease.OutQuint);
            transform.position += Vector3.forward * 0.01f;
            canvasObject.sortingOrder += 1;
            onCharacterBarObjectHovered?.Invoke(playerCharacter);
        }

        private void OnButtonUnhovered()
        {
            transform.DOKill();
            transform.DOScale(originalScale, 0.35f).SetEase(Ease.OutQuint);
            transform.position += -Vector3.forward * 0.01f;
            canvasObject.sortingOrder -= 1;
            onCharacterBarObjectUnhovered?.Invoke(playerCharacter);
        }
        
        public void SetCharacter(Character _character)
        {
            playerCharacter = _character;

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
                    _description += $"\n- <link=power_{_power.powerName}>{_power.powerName}</link>";
                }
                hoverTooltipComponent.SetTooltipDescription(_description);
            }
            
            var _rolePortrait = await playerCharacter.GetRole().GetRolePortrait();
            characterImage.sprite = _rolePortrait;
        }
    }
}