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

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
                    "OnVoteSkipButtonPressed",
                    OnVoteSkipButtonPressedMessageHandler
                );
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (IsServer)
            {
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler("OnVoteSkipButtonPressed");
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
            var _writer = new FastBufferWriter(128, Unity.Collections.Allocator.Temp);
            
            try
            {
                _writer.WriteValueSafe(NetworkManager.Singleton.LocalClientId);

                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    "OnVoteSkipButtonPressed",
                    NetworkManager.ServerClientId,
                    _writer
                );
            }
            finally
            {
                _writer.Dispose();
            }
        }

        private void OnVoteSkipButtonPressedMessageHandler(ulong _senderClientId, FastBufferReader _reader)
        {
            _reader.ReadValueSafe(out ulong _senderId);
            ((VoteState)owningGameState).OnVoteSkipButtonPressed(_senderId);
        }
        
        /*[Rpc(SendTo.Server)]
        private void OnVoteSkipButtonPressedRpc(ulong _senderId)
        {
            ((VoteState)owningGameState).OnVoteSkipButtonPressed(_senderId);
        }*/
    }
}