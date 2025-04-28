using System;
using System.Collections.Generic;
using Characters;
using Extensions;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Board.UI.CharacterBar
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
                _characterBarChildTransform.ResetLocalValues();

                var _characterBarObject = new GameObject("CharacterBarObject", typeof(Image)).AddComponent<CharactersBarObject>();
                _characterBarObject.transform.SetParent(_characterBarChildTransform);
                _characterBarObject.transform.ResetLocalValues();
                
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
