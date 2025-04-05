using System;
using System.Collections;
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
        public Dictionary<ulong, List<ulong>> votesForPlayer = new();
        public float voteDuration;
        
        public float voteTimer;

        public event Action<Dictionary<ulong, List<ulong>>> onVoteRefresh;
        
        private Coroutine updateVoteTimerCoroutine;
        
        public const ulong SKIP_VOTE_ID = 999;
        
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

            if (votesForPlayer.Values.Any(voteList => voteList.Contains(_senderId)))
            {
                Debug.LogWarning(_senderId + " has already voted.");
                return;
            }

            if (!votesForPlayer.ContainsKey(_votedPlayerId))
            {
                votesForPlayer[_votedPlayerId] = new List<ulong>();
            }
            votesForPlayer[_votedPlayerId].Add(_senderId);

            gameManager.DoStateMethodRpc(GetType().FullName, nameof(OnRefreshPlayerVotesRpc), 
                new NetworkSerializableObject[]
                {
                    new(votesForPlayer.Keys.ToArray()), 
                    new(votesForPlayer.Values.SelectMany(v => v).ToArray()),
                    new(votesForPlayer.Values.Select(v => (ulong)v.Count).ToArray())
                }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));
            
            if (votesForPlayer.Values.Sum(voteList => voteList.Count) >= gameManager.characters.Count)
            {
                voteTimer = Mathf.Min(voteTimer, 5);
            }
        }
        
        private void OnRefreshPlayerVotesRpc(ulong[] playerIds, ulong[] votes, ulong[] voteCounts)
        {
            votesForPlayer.Clear();
            int index = 0;
            for (int i = 0; i < playerIds.Length; i++)
            {
                votesForPlayer[playerIds[i]] = new List<ulong>();
                for (int j = 0; j < (int)voteCounts[i]; j++)
                {
                    votesForPlayer[playerIds[i]].Add(votes[index++]);
                }
            }
            
            onVoteRefresh?.Invoke(votesForPlayer);
        }
        
        private void UpdateVoteTimerRpc(float _newVoteTimer)
        {
            if (gameManager.IsServer)
            {
                return;
            }

            voteTimer = _newVoteTimer;
        }
        
        private IEnumerator UpdateVoteTimerCoroutine()
        {
            while (true)
            {
                gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateVoteTimerRpc), 
                    new NetworkSerializableObject[] { new(voteTimer)}, 
                    new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));
                yield return new WaitForSeconds(1);
            }
        }
        
        public void OnVoteSkipButtonPressed(ulong _senderId)
        {
            Assert.IsTrue(gameManager.IsServer, "OnVoteSkipButtonPressed can only be called on server");
            
            OnPlayerVotedRpc(_senderId, SKIP_VOTE_ID);
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
                votesForPlayer.Add(_character.ownerClientId, new List<ulong>());
            }
            votesForPlayer.Add(SKIP_VOTE_ID, new List<ulong>());
            
            voteTimer = voteDuration;
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(OnRefreshPlayerVotesRpc), 
                new NetworkSerializableObject[]
                {
                    new(votesForPlayer.Keys.ToArray()), 
                    new(Array.Empty<ulong>()),
                    new(votesForPlayer.Values.Select(v => (ulong)v.Count).ToArray())
                }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));

            updateVoteTimerCoroutine = gameManager.StartCoroutine(UpdateVoteTimerCoroutine());
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
            gameManager.StopCoroutine(updateVoteTimerCoroutine);
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();

            GameObject _newSelectPanelPlayer = SelectPanelPlayer.CreatePannel(stateUI.transform);
            var _voteSelectPanel = _newSelectPanelPlayer.AddComponent<VoteSelectPanel>();
            _voteSelectPanel.voteState = this;
            _voteSelectPanel.onPlayerVoted += OnPlayerVoted;

            if (!gameManager.IsHost)
            {
                voteTimer -= Time.deltaTime;
            }
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
            
            
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}
