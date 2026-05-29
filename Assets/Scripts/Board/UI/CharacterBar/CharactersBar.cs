#region

using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Board.UI.CharacterBar
{
    public class CharactersBar : NetworkBehaviour
    {
        public Transform charactersBarParent;
        
        [SerializeField] private GameObject characterBarObjectPrefab;
        [SerializeField] private GameObject factionLabelPrefab;
        [SerializeField] private GameObject factionGroupPrefab;
        
        [Header("Spacing Settings")]
        [SerializeField] private float intraGroupSpacing = 5f;
        [SerializeField] private float interGroupSpacing = 40f;
        [SerializeField] private float titleToGroupSpacing = 10f;
        
        public List<CharactersBarObject> charactersBarObjects = new();
        
        public event Action<Character> onCharacterBarClicked;
        public event Action<Character> onCharacterBarHovered;
        public event Action<Character> onCharacterBarUnhovered;

        private void Start()
        {
            GameManager.instance.characterManager.onCharactersListUpdated += OnCharacterListUpdated;
        }

        private void OnCharacterListUpdated(List<Character> _characters)
        {
            foreach (var _character in _characters)
            {
                var _characterBarObject = charactersBarObjects.Find(_obj => _obj.playerCharacter.ownerClientId.Value == _character.ownerClientId.Value);
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

        private AwakeningState _awakeningState;

        private AwakeningState GetAwakeningState()
        {
            if (_awakeningState == null && GameManager.instance != null)
            {
                _awakeningState = (AwakeningState)GameManager.instance.GetGameStates(typeof(AwakeningState)).FirstOrDefault();
            }
            return _awakeningState;
        }

        public IEnumerable<Character> SortCharacters(IEnumerable<Character> _characters)
        {
            return SortCharacters(_characters, GetAwakeningState());
        }

        public IEnumerable<Character> SortCharacters(IEnumerable<Character> _characters, AwakeningState _state)
        {
            return _characters
                .OrderBy(_c => _state != null ? _state.GetAwakeningLayerIndex(_c.role) : int.MaxValue)
                .ThenBy(_c => _c.role != null ? (int)_c.role.roleID : int.MaxValue)
                .ThenBy(_c => _c.ownerClientId.Value);
        }

        public void ResetCharactersBar(List<Character> _characters)
        {
            // Set inter-group spacing on the main parent layout
            if (charactersBarParent.TryGetComponent<UnityEngine.UI.HorizontalLayoutGroup>(out var _parentHlg))
            {
                _parentHlg.spacing = interGroupSpacing;
            }

            for (int i = 0; i < charactersBarParent.childCount; i++)
            {
                Destroy(charactersBarParent.GetChild(i).gameObject);
            }

            charactersBarObjects.Clear();
            
            var _sortedCharacters = SortCharacters(_characters).ToList();
            var _groups = _sortedCharacters.GroupBy(_c => _c.role != null ? _c.role.factionType : FactionType.unknown);

            foreach (var _group in _groups)
            {
                FactionType _faction = _group.Key;
                Transform _currentGroupContainer = null;

                if (factionGroupPrefab != null)
                {
                    GameObject _groupObj = Instantiate(factionGroupPrefab, charactersBarParent);
                    
                    // Set vertical spacing between title and icons
                    if (_groupObj.TryGetComponent<UnityEngine.UI.VerticalLayoutGroup>(out var _groupVlg))
                    {
                        _groupVlg.spacing = titleToGroupSpacing;
                    }

                    // Set faction title
                    var _tmp = _groupObj.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                    if (_tmp != null)
                    {
                        _tmp.text = _faction.ToString().ToUpper();
                    }

                    // Find the container for icons and set intra-group spacing
                    _currentGroupContainer = _groupObj.transform.Find("CharactersContainer");
                    if (_currentGroupContainer != null)
                    {
                        if (_currentGroupContainer.TryGetComponent<UnityEngine.UI.HorizontalLayoutGroup>(out var _groupHlg))
                        {
                            _groupHlg.spacing = intraGroupSpacing;
                        }
                    }
                    else
                    {
                        _currentGroupContainer = _groupObj.transform; // Fallback
                    }
                }
                else
                {
                    _currentGroupContainer = charactersBarParent;
                }

                foreach (Character _character in _group)
                {
                    GameObject _characterBarChild = Instantiate(characterBarObjectPrefab, _currentGroupContainer);

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
