using System;
using System.Collections.Generic;
using System.Threading;
using Characters;
using Cysharp.Threading.Tasks;
using Extensions;
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
                GameObject _characterBarChild = new GameObject(_character.GetRole().roleName.ToString(), typeof(RectTransform));
                var _characterBarChildTransform = _characterBarChild.transform;
                _characterBarChildTransform.SetParent(charactersBarParent);
                _characterBarChildTransform.localPosition = new Vector3(0, 0, 0);
                _characterBarChildTransform.localScale = new Vector3(1, 1, 1);
                _characterBarChildTransform.localRotation = Quaternion.Euler(0, 0, 0);
                

                var _characterBarObject = new GameObject("CharacterBarObject", typeof(Image)).AddComponent<CharactersBarObject>();
                _characterBarObject.transform.SetParent(_characterBarChildTransform);
                _characterBarObject.transform.localPosition = new Vector3(0, 0, 0);
                _characterBarObject.transform.localScale = new Vector3(1, 1, 1);
                _characterBarObject.transform.localRotation = Quaternion.Euler(0, 0, 0);
                
                _characterBarObject.GetComponent<RectTransform>().SetToFullStretch();
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
