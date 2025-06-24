#region

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Board.UI.VoteCanvas;
using Characters;
using Characters.Powers;
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
        [SerializeField] private PowerDataObject takeDownThePortalPowerDataObject;
        
        [HideInInspector] public float voteTimer;

        public event Action<Dictionary<ulong, List<ulong>>> onVoteRefresh;
        
        private Coroutine updateVoteTimerCoroutine;
        
        public const ulong SKIP_VOTE_ID = GameValues.FAKE_CLIENT_ID;
        
        public static ulong mostVotedPlayer;
        
        private void OnVoteButtonClicked(Card _card)
        {
            OnPlayerVoted(_card.characterInfo.ownerClientId);
        }
        
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
            
            if (votesForPlayer.Values.Sum(voteList => voteList.Count) >= gameManager.GetCharacters().Count(_c => !_c.isFake))
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
            foreach (var _character in gameManager.GetCharacters().Where(_c => !_c.isFake))
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
            
            // Get the character who has the most votes
            var _charactersWithMostVotes = votesForPlayer.OrderByDescending(v => v.Value.Count).ToList();
            int _numberOfCharacterWithTheMostVotes = _charactersWithMostVotes.Count(v => v.Value.Count == _charactersWithMostVotes.First().Value.Count);
            if (_numberOfCharacterWithTheMostVotes == 1 && _charactersWithMostVotes.First().Key != SKIP_VOTE_ID)
            {
                Character _votedCharacter = gameManager.GetCharacters().Find(_character => _character.ownerClientId == _charactersWithMostVotes.First().Key);
                _votedCharacter.isChained = true;
                mostVotedPlayer = _votedCharacter.ownerClientId;
                gameManager.gameInfoRevealer.SetRevealLevelRpc(mostVotedPlayer, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Public, false);

                if (_votedCharacter.role.powers.Any(_p => _p.IsTheSamePower(takeDownThePortalPowerDataObject.power)))
                {
                    var _portalState = (TakeDownThePortalState)GameManager.instance.GetGameStates(typeof(TakeDownThePortalState)).First();
                    _portalState.shouldActivate = true;
                    
                    GameManager.instance.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, nameof(TakeDownThePortalState.SetMageCharacterRpc),
                        new NetworkSerializableObject[] { new(_votedCharacter.ownerClientId) },
                        new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
                    Debug.Log("should activate portal state");
                }
            }
            else
            {
                mostVotedPlayer = SKIP_VOTE_ID;
            }
            
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(UpdateMostVotedPlayer), 
                new NetworkSerializableObject[] { new(mostVotedPlayer) }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.clients));
            
            gameManager.AskForUpdateAllCharactersRpc();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();

            foreach (var _c in BoardManager.instance.visibleCards)
            {
                VoteCanvas _voteCanvas = _c.voteCanvas;
                _voteCanvas.SetVoteState(this);
                _voteCanvas.ResetVoteText();
                _voteCanvas.ActivateVoteCanvas();
                _voteCanvas.onVoteButtonClicked += OnVoteButtonClicked;
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
            
            gameManager.NextGameState();
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}
