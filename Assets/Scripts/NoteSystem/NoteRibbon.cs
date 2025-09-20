using System;
using DG.Tweening;
using UI;
using UI.Panel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NoteSystem
{
    public class NoteRibbon : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, IPointerExitHandler
    {
        [SerializeField] private NoteType noteType;
        [SerializeField] private int maxNotes = GameValues.MAX_PLAYERS;
        
        [SerializeField] private GameObject notePrefab;
        [SerializeField] private NoteChoosePanel noteChoosePanelPrefab;
        [SerializeField] private Canvas noteChoosePanelCanvas;
        [SerializeField] private RectTransform ribbonPivot;
        [SerializeField] private GridLayoutGroup gridLayoutGroup;
        [SerializeField] private CustomButton addNoteButton;
        [SerializeField] private Card card;

        [SerializeField] private Vector2 hiddenAnchoredPosition;
        private Vector2 shownAnchoredPosition = Vector2.zero;
        
        private NoteChoosePanel currentNoteChoosePanel;

        private void Awake()
        {
            ribbonPivot.anchoredPosition = hiddenAnchoredPosition;
            addNoteButton.onButtonClicked += OnAddNoteButtonClicked;
        }

        private void OnAddNoteButtonClicked()
        {
            if (currentNoteChoosePanel != null)
            {
                return;
            }
            
            currentNoteChoosePanel = Instantiate(noteChoosePanelPrefab, noteChoosePanelCanvas.transform);
            currentNoteChoosePanel.SetTarget(card.characterInfo.ownerClientId, noteType);
        }

        public void OnPointerEnter(PointerEventData _eventData)
        {
            ribbonPivot.DOAnchorPos(shownAnchoredPosition, 0.5f).SetEase(Ease.OutQuint);
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
            
            ribbonPivot.DOAnchorPos(hiddenAnchoredPosition, 0.5f).SetEase(Ease.OutQuint);
        }
    }
}
