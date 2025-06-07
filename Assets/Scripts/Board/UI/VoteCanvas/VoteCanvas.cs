using System;
using System.Collections.Generic;
using System.Linq;
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
        
        private int voteCount = 0;
        
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

        public void ResetVoteText()
        {
            votesText.text = "N'a pas voté";
        }
        
        public void ShowVoteCount()
        {
            votesText.text = $"Votes: {voteCount}";
        }
        
        private void OnVoteRefresh(Dictionary<ulong, List<ulong>> _votes)
        {
            if (_votes.TryGetValue(card.characterInfo.ownerClientId, out var voters))
            {
                voteCount = voters.Count;
            }
            bool _hasVoted = _votes.Any(_vote => _vote.Value.Contains(card.characterInfo.ownerClientId));
            votesText.text = (_hasVoted) ? "A voté" : "N'a pas voté";
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
        
        public void ShowCanvas()
        {
            rectTransform.DOAnchorPos(moveDirection * rectTransform.sizeDelta.y / 2f, 0.5f).SetEase(Ease.OutQuint);
        }

        public void HideCanvas()
        {
            rectTransform.DOAnchorPos(moveDirection * rectTransform.sizeDelta.y, 0.5f).SetEase(Ease.OutQuint);
        }
    }
}
