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
        
        [SerializeField] private GameObject characterBarObjectPrefab;
        
        public event Action<Character> onCharacterBarClicked;

        public void ResetCharactersBar(List<Character> _characters)
        {
            for (int i = 0; i < charactersBarParent.childCount; i++)
            {
                Destroy(charactersBarParent.GetChild(i).gameObject);
            }

            foreach (Character _character in _characters)
            {
                GameObject _characterBarChild = Instantiate(characterBarObjectPrefab, charactersBarParent);

                var _characterBarObject = _characterBarChild.GetComponent<CharactersBarObject>();
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
