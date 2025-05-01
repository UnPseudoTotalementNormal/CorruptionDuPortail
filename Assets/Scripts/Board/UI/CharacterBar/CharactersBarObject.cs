using System;
using Characters;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace Board.UI.CharacterBar
{
    public class CharactersBarObject : MonoBehaviour
    {
        [HideInInspector] public Character playerCharacter;
        
        [SerializeField] private Image characterImage;

        [SerializeField] private Canvas canvasObject;
        
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
            var _rolePortrait = await playerCharacter.GetRole().GetRolePortrait();
            characterImage.sprite = _rolePortrait;
        }
    }
}