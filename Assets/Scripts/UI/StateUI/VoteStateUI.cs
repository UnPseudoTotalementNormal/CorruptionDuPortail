#region

using GameLogic.GameStates;
using TMPro;
using UnityEngine;

#endregion

namespace UI
{
    public class VoteStateUI : StateUI
    {
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text skipVoteAmountText;

        private void Update()
        {
            if (!owningGameState.IsStateActive())
            {
                return;
            }
            
            UpdateTimerText(((VoteState)owningGameState).voteTimer);
            skipVoteAmountText.text = "Skip";
        }
        
        private void UpdateTimerText(float _timeLeft)
        {
            timerText.text = _timeLeft.ToString("0");
        }

        public void OnVoteSkipButtonPressed()
        {
            // A skip is a vote for SKIP_VOTE_ID. Route through the WORKING player-vote dispatch
            // (VoteState.OnPlayerVoted -> DoStateMethodRpc -> OnPlayerVotedRpc on the spawned GameManager).
            // The old NetworkAction path dead-ended: its server handler attached only in OnNetworkSpawn,
            // which never fires — VoteStateUI is a NetworkBehaviour on a NetworkObject-less, never-spawned prefab.
            ((VoteState)owningGameState).OnPlayerVoted(VoteState.SKIP_VOTE_ID);
        }
    }
}