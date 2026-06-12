#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Board;
using Board.UI.VoteCanvas;
using Characters;
using Characters.Powers;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using Network;
using UI.SelectPanels;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

namespace GameLogic.GameStates
{
    [Serializable]
    [CreateAssetMenu(fileName = "VoteState", menuName = "GameStates/VoteState")]
    public class VoteState : GameState
    {
        public Dictionary<ulong, List<ulong>> votesForPlayer = new();
        public float voteDuration;
        
        [HideInInspector] public float voteTimer;

        public event Action<Dictionary<ulong, List<ulong>>> onVoteRefresh;
        
        private Coroutine updateVoteTimerCoroutine;
        
        public const ulong SKIP_VOTE_ID = GameValues.FAKE_CLIENT_ID;
        
        public static ulong mostVotedPlayer;
        
        private void OnVoteButtonClicked(Card _card)
        {
            OnPlayerVoted(_card.characterInfo.ownerClientId.Value);
        }
        
        public void OnPlayerVoted(ulong _playerId)
        {
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(OnPlayerVotedRpc),
                new NetworkSerializableObject[]
                {
                    new(characterManager.GetLocalClientId()),
                    new(_playerId),
                }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
        }

        private void OnPlayerVotedRpc(ulong _senderId, ulong _votedPlayerId)
        {
            Assert.IsTrue(gameManager.IsServer, "OnPlayerVotedRpc can only be called on server");

            if (!CanVote(_senderId))
            {
                Debug.LogWarning($"Player {_senderId} tried to vote for player {_votedPlayerId} but cannot vote.");
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
            
            if (votesForPlayer.Values.Sum(voteList => voteList.Count) >=
                characterManager.GetCharacters().Count(_c => CanVote(_c.ownerClientId.Value, true)))
            {
                voteTimer = Mathf.Min(voteTimer, 5);
            }
        }
        
        public bool CanVote(ulong _playerId, bool _ignoreAlreadyVoted = false)
        {
            Assert.IsTrue(gameManager.IsServer, "CanVote can only be called on server");
            
            if (!_ignoreAlreadyVoted && votesForPlayer.Values.Any(_voteList => _voteList.Contains(_playerId)))
            {
                return false; // Player has already voted
            }

            Character _character = characterManager.GetCharacter(_playerId, false);
            if (_character == null || _character.isEliminated.Value || _character.isFake)
            {
                return false; // Player is eliminated or does not exist or is a fake character
            }

            return true;
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
        
        private void UpdateMostVotedPlayer(ulong _lastVotedPlayer)
        {
            mostVotedPlayer = _lastVotedPlayer;
        }
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            votesForPlayer.Clear();
            foreach (var _character in characterManager.GetCharacters().Where(_c => !_c.isFake))
            {
                votesForPlayer.Add(_character.ownerClientId.Value, new List<ulong>());
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
            
            // Story 2.9 — vote-count → outcome is a pure Domain POCO (VoteTally). The adapter maps the vote buckets
            // (in insertion order — the stable-sort tie-break contract) and applies the returned decision (NFR4).
            var _votes = new List<VoteCount>();
            foreach (var _kvp in votesForPlayer)
            {
                _votes.Add(new VoteCount(_kvp.Key, _kvp.Value.Count));
            }

            ulong _winner = new VoteTally().Resolve(_votes, SKIP_VOTE_ID);
            mostVotedPlayer = _winner;
            if (_winner != SKIP_VOTE_ID)
            {
                Character _votedCharacter = characterManager.GetCharacters().Find(_character => _character.ownerClientId.Value == _winner);
                ChainingManager.instance.AddCharacterToChainingList(_votedCharacter.ownerClientId.Value);
            }
            
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateMostVotedPlayer), 
                new NetworkSerializableObject[] { new(mostVotedPlayer) }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));
            
            characterManager.AskForUpdateAllCharactersRpc();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
            ActivateVoteUI().Forget();
        }

        private async UniTaskVoid ActivateVoteUI()
        {
            try
            {
                await BoardManager.instance.ShowAllPlayerCards();

                foreach (var _c in BoardManager.instance.visibleCards)
                {
                    VoteCanvas _voteCanvas = _c.voteCanvas;
                    _voteCanvas.SetVoteState(this);
                    _voteCanvas.ResetVoteText();
                    _voteCanvas.ActivateVoteCanvas();
                    _voteCanvas.onVoteButtonClicked += OnVoteButtonClicked;
                }
            }
            catch (OperationCanceledException)
            {
                // Annulation normale (changement d'état / destruction) : sortie silencieuse
            }
            catch (Exception e)
            {
                Debug.LogError($"Erreur dans VoteState.ActivateVoteUI: {e}");
            }
        }

        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
            
            BoardManager.instance.visibleCards.ForEach(_c => _c.voteCanvas.DeactivateVoteCanvas());
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();
            
            voteTimer -= Time.deltaTime;
            if (voteTimer > 0)
            {
                return;
            }
            
            Loop.NextGameState();
        }

        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}
