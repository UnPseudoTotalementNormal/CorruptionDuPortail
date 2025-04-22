using Characters;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Board.UI
{
    public class CharactersBarObject : MonoBehaviour
    {
        public Character playerCharacter;
        
        public void SetCharacter(Character _character)
        {
            playerCharacter = _character;

            UpdateCharacter();
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