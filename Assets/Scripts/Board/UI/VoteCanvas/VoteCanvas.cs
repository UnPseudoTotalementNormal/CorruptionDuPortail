using System;
using System.Collections.Generic;
using DG.Tweening;
using GameLogic.GameStates;
using TMPro;
using UI;
using UnityEngine;

namespace Board.UI.VoteCanvas
{
    public class VoteCanvas : MonoBehaviour
    {
        private Card card;
        [SerializeField] private RectTransform rectTransform;
        private VoteState voteState;
        
        public CustomButton voteButton;
        public TMP_Text votesText;
        
        public Vector2 moveDirection = Vector2.up;
        
        public event Action<Card> onVoteButtonClicked;
        
        private void Awake()
        {
            card = GetComponentInParent<Card>();
            DeactivateVoteCanvas();
        }

        public void SetVoteState(VoteState _voteState)
        {
            voteState = _voteState;
        }

        public void ActivateVoteCanvas()
        {
            ShowCanvas();
            card.onCardHovered += OnCardHovered;
            card.onCardUnhovered += OnCardUnhovered;
            voteButton.onButtonClicked += OnVoteButtonClicked;
            if (voteState)
            {
                voteState.onVoteRefresh += OnVoteRefresh;
            }
        }

        public void DeactivateVoteCanvas()
        {
            HideCanvas();
            card.onCardHovered -= OnCardHovered;
            card.onCardUnhovered -= OnCardUnhovered;
            voteButton.onButtonClicked -= OnVoteButtonClicked;
            if (voteState)
            {
                voteState.onVoteRefresh -= OnVoteRefresh;
            }
        }

        public void ResetVotes()
        {
            votesText.text = "Votes: 0";
        }
        
        private void OnVoteRefresh(Dictionary<ulong, List<ulong>> _votes)
        {
            votesText.text = "Votes: " + (_votes.TryGetValue(card.characterInfo.ownerClientId, out var _vote) 
                ? _vote.Count.ToString() 
                : "0");
        }
        
        private void OnVoteButtonClicked()
        {
            onVoteButtonClicked?.Invoke(card);
        }

        private void OnCardHovered(Card _card)
        {
            rectTransform.DOAnchorPos(Vector2.zero, 0.5f).SetEase(Ease.OutQuint);
        }

        private void OnCardUnhovered(Card _card)
        {
            rectTransform.DOAnchorPos(moveDirection * rectTransform.sizeDelta.y / 2f, 0.5f).SetEase(Ease.OutQuint);
        }
        
        private void ShowCanvas()
        {
            rectTransform.DOAnchorPos(moveDirection * rectTransform.sizeDelta.y / 2f, 0.5f).SetEase(Ease.OutQuint);
        }

        private void HideCanvas()
        {
            rectTransform.DOAnchorPos(moveDirection * rectTransform.sizeDelta.y, 0.5f).SetEase(Ease.OutQuint);
        }
    }
}
