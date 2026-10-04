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

        // NET-06: server-side "votes are accepted" window, open from OnStartStateServer until the tally in
        // OnEndStateServer. A vote RPC landing outside it (latency at the end of the timer) is ignored.
        private bool _isVoteOpenServer;
        
        private void OnVoteButtonClicked(Card _card)
        {
            OnPlayerVoted(_card.characterInfo.ownerClientId.Value);
        }
        
        public void OnPlayerVoted(ulong _playerId)
        {
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(OnPlayerVotedRpc),
                new NetworkSerializableObject[]
                {
                    new(CharacterQuery.GetLocalClientId()),
                    new(_playerId),
                }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
        }

        private void OnPlayerVotedRpc(ulong _senderId, ulong _votedPlayerId)
        {
            Assert.IsTrue(gameManager.IsServer, "OnPlayerVotedRpc can only be called on server");

            // NET-06: a vote that arrives after the tally (RPC latency at the end of the timer) must not be recorded
            // nor broadcast — the outcome is already decided (deferred-work: late/stale vote race).
            if (!_isVoteOpenServer)
            {
                Debug.LogWarning($"[VOTE] Late vote from {_senderId} for {_votedPlayerId} ignored: the vote is closed.");
                return;
            }

            // NET-06: the voter is the RPC SENDER. Only the host may vote on behalf of another identity (simulated bots
            // >= 100 and the dev possession flow both run on the host).
            ulong _rpcSender = gameManager.CurrentStateRpcSenderId;
            if (_rpcSender != Unity.Netcode.NetworkManager.ServerClientId && _senderId != _rpcSender)
            {
                Debug.LogWarning($"[VOTE] Client {_rpcSender} tried to vote as {_senderId}; recording it as its own vote.");
                _senderId = _rpcSender;
            }

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
                CharacterQuery.GetCharacters().Count(_c => CanVote(_c.ownerClientId.Value, true)))
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

            Character _character = CharacterQuery.GetCharacter(_playerId, false);
            if (_character == null || _character.isEliminated.Value || _character.isFake)
            {
                return false; // Player is eliminated or does not exist or is a fake character
            }

            // [LEAVE] Phase 2 — re-exclude a DEPARTED real client (owner ruling: a merely-chained-but-PRESENT
            // player stays eligible, so we do NOT key on isChained). The original game excluded a disconnected
            // player because the old handler fakified them and this method already drops isFake; Phase 1 replaced
            // fakify with chaining, losing that. Re-key it on the true "this real client has left" discriminator
            // (GameManager's departed-set). Bots (id >= 100) are host-simulated and always present — never excluded.
            if (_playerId < 100 && gameManager.HasClientLeft(_playerId))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// [LEAVE] Phase 2 (epic-player-leave-stability) — server hook the leave pipeline calls after a mid-game
        /// leave. The eligible-voter denominator (<see cref="CanVote"/>) has just shrunk (the DEPARTED leaver is
        /// now excluded via GameManager's departed-set — chained-but-present players still count), so an open vote
        /// where everyone still present has already voted should close NOW rather than waiting out the timer.
        /// Recomputes the auto-close threshold — the same one <see cref="OnPlayerVotedRpc"/> checks — and collapses
        /// the timer when it is already met. Invoked only while this state is current
        /// (GameManager.UnblockCurrentStateAfterLeave).
        /// </summary>
        public void OnPlayerLeftServer(ulong _ownerId)
        {
            Assert.IsTrue(gameManager.IsServer, "OnPlayerLeftServer can only be called on server");

            int _castVotes = votesForPlayer.Values.Sum(_voteList => _voteList.Count);
            int _eligibleVoters = CharacterQuery.GetCharacters().Count(_c => CanVote(_c.ownerClientId.Value, true));

            if (_castVotes >= _eligibleVoters)
            {
                Debug.Log($"[LEAVE] VoteState: after {_ownerId} left, {_castVotes} vote(s) meet the reduced denominator ({_eligibleVoters}) — collapsing the timer to auto-close.");
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
            _isVoteOpenServer = true;
            votesForPlayer.Clear();
            foreach (var _character in CharacterQuery.GetCharacters().Where(_c => !_c.isFake))
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
            _isVoteOpenServer = false;
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
                Character _votedCharacter = CharacterQuery.GetCharacters().Find(_character => _character.ownerClientId.Value == _winner);
                // [LEAVE] Phase 2 — null-guard: the most-voted seat may have been removed (leaver) between the
                // vote and this tally. If it is gone there is nothing to chain — skip instead of NRE-ing.
                if (_votedCharacter != null)
                {
                    chainingManager.AddCharacterToChainingList(_votedCharacter.ownerClientId.Value);
                }
                else
                {
                    Debug.Log($"[LEAVE] VoteState: most-voted player {_winner} is no longer present — skipping chaining.");
                }
            }
            
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateMostVotedPlayer), 
                new NetworkSerializableObject[] { new(mostVotedPlayer) }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));
            
            Command.AskForUpdateAllCharactersRpc();
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
                await boardManager.ShowAllPlayerCards();

                foreach (var _c in boardManager.visibleCards)
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
            
            boardManager.visibleCards.ForEach(_c => _c.voteCanvas.DeactivateVoteCanvas());
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
