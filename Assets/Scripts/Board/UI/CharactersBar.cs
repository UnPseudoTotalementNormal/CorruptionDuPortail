using System;
using System.Collections.Generic;
using System.Threading;
using Characters;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Board.UI
{
    public class CharactersBar : NetworkBehaviour
    {
        public Transform charactersBarParent;
        
        public event Action<Character> onCharacterBarClicked;

        public void ResetCharactersBar(List<Character> _characters)
        {
            for (int i = 0; i < charactersBarParent.childCount; i++)
            {
                Destroy(charactersBarParent.GetChild(i).gameObject);
            }

            foreach (Character _character in _characters)
            {
                GameObject _characterBarChild = new GameObject(_character.GetRole().roleName.ToString(), typeof(Image));
                var _characterBarTransform = _characterBarChild.transform;
                _characterBarTransform.SetParent(charactersBarParent);
                _characterBarTransform.localPosition = new Vector3(0, 0, 0);
                _characterBarTransform.localScale = new Vector3(1, 1, 1);
                _characterBarTransform.localRotation = Quaternion.Euler(0, 0, 0);

                var _characterBarObject = _characterBarChild.AddComponent<CharactersBarObject>();
                _characterBarObject.SetCharacter(_character);
                _characterBarObject.onCharacterBarObjectClicked += (_characterClicked) =>
                {
                    onCharacterBarClicked?.Invoke(_characterClicked);
                };
            }
        }
        
        public void DestroyCharactersBar()
        {
            for (int i = 0; i < charactersBarParent.childCount; i++)
            {
                Destroy(charactersBarParent.GetChild(i).gameObject);
            }
        }
    }
}
