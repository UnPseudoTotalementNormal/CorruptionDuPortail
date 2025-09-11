#region

using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Board.UI.CharacterBar
{
    public class CharactersBar : NetworkBehaviour
    {
        public Transform charactersBarParent;
        
        [SerializeField] private GameObject characterBarObjectPrefab;
        
        public List<CharactersBarObject> charactersBarObjects = new();
        
        public event Action<Character> onCharacterBarClicked;
        public event Action<Character> onCharacterBarHovered;
        public event Action<Character> onCharacterBarUnhovered;

        private void Start()
        {
            GameManager.instance.onCharactersListUpdated += OnCharacterListUpdated;
        }

        private void OnCharacterListUpdated(List<Character> _characters)
        {
            foreach (var _character in _characters)
            {
                var _characterBarObject = charactersBarObjects.Find(_obj => _obj.playerCharacter.ownerClientId == _character.ownerClientId);
                if (_characterBarObject)
                {
                    _characterBarObject.SetCharacter(_character);
                }
            }
        }
        
        public List<CharactersBarObject> GetCharacterBarObject(Role _role)
        {
            return charactersBarObjects.FindAll(_obj => _obj.playerCharacter.role.IsTheSameRole(_role));
        }

        public void ResetCharactersBar(List<Character> _characters)
        {
            for (int i = 0; i < charactersBarParent.childCount; i++)
            {
                Destroy(charactersBarParent.GetChild(i).gameObject);
            }

            charactersBarObjects.Clear();
            
            foreach (Character _character in _characters.OrderBy(_ => UnityEngine.Random.value).ToList())
            {
                GameObject _characterBarChild = Instantiate(characterBarObjectPrefab, charactersBarParent);

                var _characterBarObject = _characterBarChild.GetComponent<CharactersBarObject>();
                _characterBarObject.SetCharacter(_character);
                _characterBarObject.onCharacterBarObjectClicked += (_characterClicked) =>
                {
                    onCharacterBarClicked?.Invoke(_characterClicked);
                };
                _characterBarObject.onCharacterBarObjectHovered += (_characterHovered) =>
                {
                    onCharacterBarHovered?.Invoke(_characterHovered);
                };
                _characterBarObject.onCharacterBarObjectUnhovered += (_characterUnhovered) =>
                {
                    onCharacterBarUnhovered?.Invoke(_characterUnhovered);
                };
                
                charactersBarObjects.Add(_characterBarObject);
            }
        }
        
        public void DestroyCharactersBar()
        {
            for (int i = 0; i < charactersBarParent.childCount; i++)
            {
                Destroy(charactersBarParent.GetChild(i).gameObject);
            }
            
            charactersBarObjects.Clear();
        }
    }
}
