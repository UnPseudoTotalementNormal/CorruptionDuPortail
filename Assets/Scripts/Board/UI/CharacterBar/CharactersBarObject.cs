using System;
using Characters;
using Cysharp.Threading.Tasks;
using UI;
using UnityEngine;
using UnityEngine.UI;

namespace Board.UI.CharacterBar
{
    public class CharactersBarObject : MonoBehaviour
    {
        public Character playerCharacter;

        private CustomButton customButton;
        
        public event Action<Character> onCharacterBarObjectClicked;

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
        }

        public void SetCharacter(Character _character)
        {
            playerCharacter = _character;

            _ = UpdateCharacter();
        }

        private async UniTaskVoid UpdateCharacter()
        {
            var _rolePortrait = await playerCharacter.GetRole().GetRolePortrait();
            if (TryGetComponent(out Image _image))
            {
                _image.sprite = _rolePortrait;
            }
        }
    }
}