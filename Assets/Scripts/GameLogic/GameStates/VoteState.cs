using System;
using System.Collections.Generic;
using System.Linq;
using Network;
using UI.SelectPanels;
using UnityEngine;
using UnityEngine.Assertions;

namespace GameLogic.GameStates
{
    [Serializable]
    [CreateAssetMenu(fileName = "VoteState", menuName = "GameStates/VoteState")]
    public class VoteState : GameState
    {
        public Dictionary<ulong, int> votesForPlayer = new();
        public float voteDuration;
        
        private float voteTimer;

        public event Action<Dictionary<ulong, int>> onVoteRefresh;
        
        public void OnPlayerVoted(ulong _playerId)
        {
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(OnPlayerVotedRpc),
                new NetworkSerializableObject[]
                {
                    new(gameManager.NetworkManager.LocalClientId),
                    new(_playerId),
                }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
        }

        private void OnPlayerVotedRpc(ulong _senderId, ulong _votedPlayerId)
        {
            Assert.IsTrue(gameManager.IsServer, "OnPlayerVotedRpc can only be called on server");
            
            if (!votesForPlayer.TryAdd(_votedPlayerId, 1))
            {
                votesForPlayer[_votedPlayerId]++;
            }
            
            Debug.Log(_senderId + " voted for " + _votedPlayerId);

            gameManager.DoStateMethodRpc(GetType().FullName, nameof(OnRefreshPlayerVotesRpc), 
                new NetworkSerializableObject[]
                {
                    new(votesForPlayer.Keys.ToArray()), 
                    new(votesForPlayer.Values.ToArray()),
                }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));
        }
        
        private void OnRefreshPlayerVotesRpc(ulong[] playerIds, int[] votes)
        {
            votesForPlayer.Clear();
            for (int i = 0; i < playerIds.Length; i++)
            {
                votesForPlayer.Add(playerIds[i], votes[i]);
            }
            
            onVoteRefresh?.Invoke(votesForPlayer);
        }
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            votesForPlayer.Clear();
            foreach (var _character in gameManager.characters)
            {
                votesForPlayer.Add(_character.ownerClientId, 0);
            }
            voteTimer = voteDuration;
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(OnRefreshPlayerVotesRpc), 
                new NetworkSerializableObject[]
                {
                    new(votesForPlayer.Keys.ToArray()), 
                    new(votesForPlayer.Values.ToArray()),
                }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();

            GameObject _newSelectPanelPlayer = SelectPanelPlayer.CreatePannel(stateUI.transform);
            var _voteSelectPanel = _newSelectPanelPlayer.AddComponent<VoteSelectPanel>();
            _voteSelectPanel.voteState = this;
            _voteSelectPanel.onPlayerVoted += OnPlayerVoted;
        }
        
        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();
            
            voteTimer -= Time.deltaTime;
            if (voteTimer > 0)
            {
                return;
            }
            
            gameManager.NextGameState();
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}