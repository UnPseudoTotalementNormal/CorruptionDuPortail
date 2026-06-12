using System.Collections.Generic;
using System.Linq;
using Board;
using Board.UI.CharacterBar;
using Characters;
using DG.Tweening;
using GameLogic;
using UI;
using UI.Panel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NoteSystem
{
    public class NoteRibbon : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, IPointerExitHandler, IPanelOpen
    {
        [SerializeField] private NoteType noteType;
        [SerializeField] private int maxNotes = GameValues.MAX_PLAYERS;
        
        [SerializeField] private CharactersBarObject notePrefab;
        [SerializeField] private NoteChoosePanel noteChoosePanelPrefab;
        [SerializeField] private Canvas noteChoosePanelCanvas;
        [SerializeField] private RectTransform ribbonPivot;
        [SerializeField] private GridLayoutGroup gridLayoutGroup;
        [SerializeField] private CustomButton addNoteButton;
        [SerializeField] private Card card;

        [SerializeField] private Vector2 hiddenAnchoredPosition;
        private Vector2 shownAnchoredPosition = Vector2.zero;
        private Vector2 originalSizeDelta;
        
        private NoteChoosePanel currentNoteChoosePanel;

        [SerializeField] private int noteColumns = 3; 
        
        public bool isPanelOpen { get; protected set;}
        public bool isChoosePanelOpen => currentNoteChoosePanel != null;
        
        private List<NoteRibbon> siblingsRibbons = new();

        private void Awake()
        {
            originalSizeDelta = GetComponent<RectTransform>().sizeDelta;
            ribbonPivot.anchoredPosition = hiddenAnchoredPosition;
            addNoteButton.onButtonClicked += OnAddNoteButtonClicked;

            var _noteManager = NoteManager.instance;
            switch (noteType)
            {
                case NoteType.Confirmed:
                    _noteManager.onConfirmedRolesByPlayerModified += OnNotesModified;
                    break;
                case NoteType.Possible:
                    _noteManager.onPossibleRolesByPlayerModified += OnNotesModified;
                    break;
                case NoteType.Excluded:
                    _noteManager.onExcludedRolesByPlayerModified += OnNotesModified;
                    break;
            }
        }

        private void Start()
        {
            foreach (var _sibling in transform.parent.GetComponentsInChildren<NoteRibbon>())
            {
                if (_sibling != this)
                {
                    siblingsRibbons.Add(_sibling);
                }
            }
        }

        private void OnNotesModified(ulong _playerNoted, List<Role> _roles)
        {
            if (_playerNoted != card.characterInfo.ownerClientId.Value)
            {
                return;
            }
            
            RedrawNotes(_roles);
            if (isPanelOpen)
            {
                var _notesCount = _roles.Count;
                if (addNoteButton.gameObject.activeInHierarchy)
                {
                    _notesCount++;
                }
                SetCorrectSize(_notesCount);
            }
        }

        private void SetCorrectSize(int _notesCount)
        {
            gridLayoutGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayoutGroup.constraintCount = noteColumns;

            var _cellSize = gridLayoutGroup.cellSize;
            var _spacing = gridLayoutGroup.spacing;
            var _padding = gridLayoutGroup.padding;
            int _usedColumns = Mathf.Min(noteColumns, _notesCount > 0 ? _notesCount : 1);
            int _lines = Mathf.CeilToInt((float)_notesCount / noteColumns);
            _lines = Mathf.Max(_lines, 1);

            float _width = _padding.left + _padding.right + _usedColumns * _cellSize.x + (_usedColumns - 1) * _spacing.x;
            float _height = _padding.top + _padding.bottom + _lines * _cellSize.y + (_lines - 1) * _spacing.y;

            GetComponent<RectTransform>().DOKill(false);
            GetComponent<RectTransform>().DOSizeDelta(new Vector2(_width, _height), 0.25f).SetEase(Ease.OutQuint);
        }

        private void RedrawNotes(List<Role> _roles)
        {
            foreach (Transform _child in gridLayoutGroup.transform)
            {
                if (_child.gameObject == addNoteButton.gameObject) continue;
                Destroy(_child.gameObject);
            }

            int _notesToDisplay = Mathf.Min(_roles.Count, maxNotes);
            for (int _i = 0; _i < _notesToDisplay; _i++)
            {
                var _noteObject = Instantiate(notePrefab, gridLayoutGroup.transform);
                // Story 12.2: read the character off the parent Card's injected ICharacterQuery slice (NoteManager
                // stays a singleton — recorded §4f survivor — only the CharacterManager read is decoupled here).
                _noteObject.SetCharacter(card.CharacterQuery.GetCharacter(_roles[_i].ownerClientId, false));
                var _index = _i;
                _noteObject.onCharacterBarObjectClicked += (_) =>
                {
                    NoteManager.instance.RemoveNote(card.characterInfo.ownerClientId.Value, _roles[_index], noteType);
                };
            }

            if (_roles.Count < maxNotes)
            {
                addNoteButton.gameObject.SetActive(true);
            }
            else
            {
                addNoteButton.gameObject.SetActive(false);
                currentNoteChoosePanel?.ClosePanel();
            }
        }

        private void OnAddNoteButtonClicked()
        {
            if (currentNoteChoosePanel != null || siblingsRibbons.Any(_r => _r.isChoosePanelOpen))
            {
                return;
            }
            
            currentNoteChoosePanel = Instantiate(noteChoosePanelPrefab, noteChoosePanelCanvas.transform);
            currentNoteChoosePanel.transform.SetSiblingIndex(0);
            currentNoteChoosePanel.SetTarget(card.characterInfo.ownerClientId.Value, noteType, card.CharacterQuery);
            currentNoteChoosePanel.onPanelClose += () =>
            {
                currentNoteChoosePanel = null;
                HideRibbon();
            };
        }

        public void OnPointerEnter(PointerEventData _eventData)
        {
            ShowRibbon();
        }

        public void OnPointerClick(PointerEventData _eventData)
        {
        }

        public void OnPointerExit(PointerEventData _eventData)
        {
            if (currentNoteChoosePanel != null)
            {
                return;
            }
            
            HideRibbon();
        }

        private void ShowRibbon()
        {
            isPanelOpen = true;
            ribbonPivot.DOKill(true);
            ribbonPivot.DOAnchorPos(shownAnchoredPosition, 0.5f).SetEase(Ease.OutQuint);
            var _notesCount = gridLayoutGroup.transform.childCount;
            if (!addNoteButton.gameObject.activeInHierarchy)
            {
                _notesCount--;
            }
            SetCorrectSize(_notesCount);
        }

        private void HideRibbon()
        {
            isPanelOpen = false;
            ribbonPivot.DOKill(true);
            ribbonPivot.DOAnchorPos(hiddenAnchoredPosition, 0.5f).SetEase(Ease.OutQuint);
            GetComponent<RectTransform>().DOKill(false);
            GetComponent<RectTransform>().DOSizeDelta(originalSizeDelta, 0.5f).SetEase(Ease.OutQuint);
        }
    }
}
