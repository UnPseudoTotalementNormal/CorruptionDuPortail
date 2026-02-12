using DG.Tweening;
using GameLogic;
using GameLogic.GameStates;
using TMPro;
using UnityEngine;

namespace UI
{
    public class VoteRecapStateUI: StateUI
    {
        [SerializeField] private TMP_Text voteSkipResultText;
        VoteState previousVoteState;

        public override void SetupStateUI(GameManager gameManager, GameState gameState)
        {
            base.SetupStateUI(gameManager, gameState);
            VoteRecapState voteRecapState = (VoteRecapState)owningGameState;
            voteRecapState.onShowVoteRecap += ShowVoteRecap;
            voteRecapState.onHideVoteRecap += HideVoteRecap;
            previousVoteState = gameManager.GetClosestPreviousState<VoteState>();

            voteSkipResultText.alpha = 0;
        }

        private void ShowVoteRecap()
        {
            int skipVoteCount = 0;
            if (previousVoteState != null && previousVoteState.votesForPlayer.TryGetValue(VoteState.SKIP_VOTE_ID, out var value))
            {
                skipVoteCount = value.Count;
            }
            
            voteSkipResultText.text = $"Skip votes: {skipVoteCount}";
            voteSkipResultText.DOFade(1, 0.5f);
        }

        private void HideVoteRecap()
        {
            voteSkipResultText.DOFade(0, 0.5f);
        }
    }
}