using System;
using System.Collections.Generic;
using System.Linq;
using Board.UI.CharacterBar;
using Characters;
using DG.Tweening;
using GameLogic;
using UI;
using UI.Panel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace NoteSystem
{
    public class NoteChoosePanel : MonoBehaviour, IPanelComponent, IPanelCloseEvent, IPointerClickHandler
    {
        [SerializeField] private CharactersBarObject characterNoteObjectPrefab;
        [SerializeField] private RectTransform layoutTransform;
        [SerializeField] private CustomButton closeButton;
        
        public event Action<Character> onCharacterNoteObjectClicked;
        public event Action onPanelClose;
        
        private ulong currentPlayerId;
        private NoteType currentNoteType;

        
        public bool isPanelOpen { get; protected set; }

        private void Awake()
        {
            onCharacterNoteObjectClicked += OnCharacterNoteObjectClicked;
            closeButton.onButtonClicked += ClosePanel;
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
            var _noteManager = NoteManager.instance;
            isPanelOpen = true;
            
            DisplayCharacters(currentPlayerId, _noteManager.GetNotesForPlayer(currentPlayerId, currentNoteType));

            switch (currentNoteType)
            {
                case NoteType.Confirmed:
                    _noteManager.onConfirmedRolesByPlayerModified += UpdateDisplay;
                    break;
                case NoteType.Possible:
                    _noteManager.onPossibleRolesByPlayerModified += UpdateDisplay;
                    break;
                case NoteType.Excluded:
                    _noteManager.onExcludedRolesByPlayerModified += UpdateDisplay;
                    break;
            }
            
        }

        private void DisplayCharacters(ulong _currentPlayerId, List<Role> _currentPlayerNotes)
        {
            foreach (Transform _child in layoutTransform)
            {
                Destroy(_child.gameObject);
            }
            
            var _allCharacters = GameManager.instance.GetCharacters(false);
            
            List<Character> _filteredCharacters = _allCharacters.ToList();

            _filteredCharacters.RemoveAll(_c => _currentPlayerNotes.Any(_cn => _cn.IsTheSameRole(_c.GetRole())));
                
            _filteredCharacters = _filteredCharacters
                .GroupBy(_c => _filteredCharacters.FirstOrDefault(x => x.role.IsTheSameRole(_c.role)), _c => _c)
                .Select(g => g.First())
                .ToList();

            foreach (Character _character in _filteredCharacters)
            {
                var _characterNoteObject = Instantiate(characterNoteObjectPrefab, layoutTransform);
                _characterNoteObject.SetCharacter(_character);
                _characterNoteObject.onCharacterBarObjectClicked += (_) =>
                {
                    onCharacterNoteObjectClicked?.Invoke(_character);
                };
            }
        }

        private void UpdateDisplay(ulong _ulong, List<Role> _roles)
        {
            DisplayCharacters(_ulong, _roles);
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
            Destroy(gameObject);
            onPanelClose?.Invoke();
            var _noteManager = NoteManager.instance;
            _noteManager.onConfirmedRolesByPlayerModified -= UpdateDisplay;
            _noteManager.onPossibleRolesByPlayerModified -= UpdateDisplay;
            _noteManager.onExcludedRolesByPlayerModified -= UpdateDisplay;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            
        }
    }
}