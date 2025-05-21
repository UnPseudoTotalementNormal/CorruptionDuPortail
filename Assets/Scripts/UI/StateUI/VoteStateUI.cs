#region

using GameLogic.GameStates;
using TMPro;
using Unity.Netcode;
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
            skipVoteAmountText.text = $"Skip ({((VoteState)owningGameState).votesForPlayer[VoteState.SKIP_VOTE_ID].Count.ToString()})";
        }
        
        private void UpdateTimerText(float timeLeft)
        {
            timerText.text = timeLeft.ToString("0");
        }

        public void OnVoteSkipButtonPressed()
        {
            OnVoteSkipButtonPressedRpc(gameManager.NetworkManager.LocalClientId);
        }

        [Rpc(SendTo.Server)]
        private void OnVoteSkipButtonPressedRpc(ulong _senderId)
        {
            ((VoteState)owningGameState).OnVoteSkipButtonPressed(_senderId);
        }
    }
}