#region

using GameLogic.GameStates;
using Network.Action;
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
        private NetworkAction<ulong> onVoteSkipButtonPressedByClient = new($"onVoteSkipButtonPressedVoteStateUI", true);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                onVoteSkipButtonPressedByClient += OnVoteSkipButtonPressedServer;
            }
        }

        private void OnVoteSkipButtonPressedServer(ulong _clientId)
        {
            ((VoteState)owningGameState).OnVoteSkipButtonPressed(_clientId);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (IsServer)
            {
                onVoteSkipButtonPressedByClient -= OnVoteSkipButtonPressedServer;
                onVoteSkipButtonPressedByClient.Unregister();
            }
        }

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
            onVoteSkipButtonPressedByClient?.Invoke(NetworkManager.Singleton.LocalClientId);
        }
    }
}