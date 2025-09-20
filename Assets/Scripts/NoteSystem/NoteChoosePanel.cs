using System;
using System.Collections.Generic;
using System.Linq;
using Board.UI.CharacterBar;
using Characters;
using DG.Tweening;
using GameLogic;
using UI.Panel;
using UnityEngine;
using UnityEngine.UIElements;

namespace NoteSystem
{
    public class NoteChoosePanel : MonoBehaviour, IPanelComponent
    {
        [SerializeField] private CharactersBarObject characterNoteObjectPrefab;
        [SerializeField] private RectTransform layoutTransform;
        
        public event Action<Character> onCharacterNoteObjectClicked;
        
        private ulong currentPlayerId;
        private NoteType currentNoteType;
        
        public bool isPanelOpen { get; protected set; }

        private void Awake()
        {
            onCharacterNoteObjectClicked += OnCharacterNoteObjectClicked;
        }

        private void OnCharacterNoteObjectClicked(Character _character)
        {
            NoteManager.instance.AddNote(currentPlayerId, _character.GetRole(), currentNoteType);
        }


        public void SetTarget(ulong _playerId, NoteType _noteType)
        {
            currentPlayerId = _playerId;
            currentNoteType = _noteType;
            Init();
        }

        private void Init()
        {
            isPanelOpen = true;
            DisplayCharacters();
        }

        private void DisplayCharacters()
        {
            foreach (Transform _child in layoutTransform)
            {
                Destroy(_child.gameObject);
            }
            
            var _currentPlayerNotes = NoteManager.instance.GetNotesForPlayer(currentPlayerId, currentNoteType);
            var _allCharacters = GameManager.instance.GetCharacters(false);
            
            List<Character> _filteredCharacters = _allCharacters.ToList();

            _filteredCharacters.RemoveAll(_c => 
                _currentPlayerNotes.Any(_cn => _cn.IsTheSameRole(_c.GetRole())));
                
            _filteredCharacters
                .GroupBy(c => c.GetRole())
                .Select(g => g.First())
                .Where(c => !_currentPlayerNotes.Any(n => n.IsTheSameRole(n)))
                .ToList();

            foreach (Character _character in _filteredCharacters)
            {
                var _characterNoteObject = Instantiate(characterNoteObjectPrefab, layoutTransform);
                _characterNoteObject.SetCharacter(_character);
                _characterNoteObject.onCharacterBarObjectClicked += (_) =>
                {
                    Destroy(_characterNoteObject.gameObject);
                    onCharacterNoteObjectClicked?.Invoke(_character);
                };
            }
        }
        
        public void SwitchPanelOpen()
        {
            throw new NotImplementedException();
        }

        public void TryOpenPanel()
        {
            throw new NotImplementedException();
        }

        public void OpenPanel()
        {
            throw new NotImplementedException();
        }

        public void ClosePanel()
        {
            throw new NotImplementedException();
        }
    }
}