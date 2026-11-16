using Localization;
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

        private bool isEliminatedOnVoteStart;
        private bool isChainedOnVoteStart;
        
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

        /// <summary>
        /// Shows the panel under the card. <paramref name="_withVoteButton"/> false (T23: by day the vote is cast on the
        /// cats, the cards are only to look at): the panel keeps its text ("A voté" / "N'a pas voté") but not its button.
        /// </summary>
        public void ActivateVoteCanvas(bool _withVoteButton = true)
        {
            isChainedOnVoteStart = card.characterInfo.isChained.Value;
            isEliminatedOnVoteStart = card.characterInfo.isEliminated.Value;
            voteButton.gameObject.SetActive(_withVoteButton);
            ShowCanvas();
            card.onCardHovered += OnCardHovered;
            card.onCardUnhovered += OnCardUnhovered;
            if (card.characterInfo.isEliminated.Value)
            {
                votesText.SetLoc("Éliminé");
                voteButton.GetComponentInChildren<TMP_Text>().SetLoc("Éliminé");
                return;
            }

            if (card.characterInfo.isChained.Value)
            {
                voteButton.GetComponentInChildren<TMP_Text>().SetLoc("Enchaîné");
            }
            else if (_withVoteButton)
            {
                voteButton.onButtonClicked += OnVoteButtonClicked;
            }
            
            if (voteState)
            {
                voteState.onVoteRefresh += OnVoteRefresh;
            }
        }

        public void DeactivateVoteCanvas()
        {
            HideCanvas();
            voteButton.gameObject.SetActive(true);
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
            if (card.characterInfo.isEliminated.Value)
            {
                return;
            }
            votesText.SetLoc("N'a pas voté");
        }
        
        public void ShowVoteCount(bool _useVoteStartValue)
        {
            if ((_useVoteStartValue && isEliminatedOnVoteStart) || (!_useVoteStartValue && card.characterInfo.isEliminated.Value))
            {
                votesText.SetLoc("Éliminé");
            }
            else if ((_useVoteStartValue && isChainedOnVoteStart) || (!_useVoteStartValue && card.characterInfo.isChained.Value))
            {
                votesText.SetLoc("Enchaîné");
            }
            else
            {
                votesText.SetLoc("Votes: {0}", voteCount);
            }
        }
        
        private void OnVoteRefresh(Dictionary<ulong, List<ulong>> _votes)
        {
            if (_votes.TryGetValue(card.characterInfo.ownerClientId.Value, out var voters))
            {
                voteCount = voters.Count;
            }
            bool _hasVoted = _votes.Any(_vote => _vote.Value.Contains(card.characterInfo.ownerClientId.Value));
            votesText.SetLoc(_hasVoted ? "A voté" : "N'a pas voté"); // loc
        }
        
        private void OnVoteButtonClicked()
        {
            Diagnostics.Logs.GameLog.Guarded(() => Diagnostics.Logs.GameLog.Info("vote", "vote.card.click",
                "to", card != null && card.characterInfo != null ? (object)card.characterInfo.ownerClientId.Value : null));
            onVoteButtonClicked?.Invoke(card);
        }

        private void OnCardHovered(Card _card)
        {
            rectTransform.DOAnchorPos(Vector2.zero, 0.5f).SetEase(Ease.OutQuint).SetLink(gameObject);
        }

        private void OnCardUnhovered(Card _card)
        {
            rectTransform.DOAnchorPos(moveDirection * rectTransform.sizeDelta.y / 2f, 0.5f).SetEase(Ease.OutQuint).SetLink(gameObject);
        }
        
        public void ShowCanvas()
        {
            rectTransform.DOAnchorPos(moveDirection * rectTransform.sizeDelta.y / 2f, 0.5f).SetEase(Ease.OutQuint).SetLink(gameObject);
        }

        public void HideCanvas()
        {
            rectTransform.DOAnchorPos(moveDirection * rectTransform.sizeDelta.y, 0.5f).SetEase(Ease.OutQuint).SetLink(gameObject);
        }
    }
}
