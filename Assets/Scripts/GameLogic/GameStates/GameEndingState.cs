#region

using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.WinningConditions;
using Cysharp.Threading.Tasks;
using Network;
using UnityEngine;

#endregion

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "GameEndingState", menuName = "GameStates/GameEndingState")]
    public class GameEndingState : GameState
    {
        private Dictionary<WinningTeam, ulong[]> winningTeams = new();

        /// <summary>Winners this peer received (read-only; autoplay state export).</summary>
        public IReadOnlyDictionary<WinningTeam, ulong[]> WinningTeams => winningTeams;

        public void SetWinnersServer(Dictionary<WinningTeam, HashSet<ulong>> _winningTeams)
        {
            var _winnersArray = _winningTeams
                .Select(kvp => new KeyValuePair<WinningTeam, ulong[]>(kvp.Key, kvp.Value.ToArray()))
                .ToArray();
        
            gameManager.DoStateMethodRpc(typeof(GameEndingState).FullName, nameof(SetWinnersClientRpc),
                new NetworkSerializableObject[]
                {
                    new(_winnersArray)
                }, 
                new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
        }
        
        public void SetWinnersClientRpc(KeyValuePair<WinningTeam, ulong[]>[] _winnersArray)
        {
            winningTeams = _winnersArray.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public async UniTaskVoid GameEndingAnimation()
        {
            // Tied to the board's lifetime: "Terminer la partie" during the animation unloads the scene (B10 / N4 family).
            var _boardAlive = boardManager.GetCancellationTokenOnDestroy();
            if (await boardManager.HideAllCards().AttachExternalCancellation(_boardAlive).SuppressCancellationThrow())
            {
                return;
            }
            
            foreach (var _winningTeam in winningTeams)
            {
                foreach (var _playerId in _winningTeam.Value)
                {
                    var _character = CharacterQuery.GetCharacter(_playerId, false);
                    if (_character == null)
                    {
                        continue;
                    }
                    
                    var _newCard = boardManager.AddNewCard();
                    _newCard.SetInfo(_character);
                    _ = _newCard.ShowPseudoWithRevealedInfo();
                    _ = _newCard.ShowBackSide(true);
                }
            }
            
            if (await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: _boardAlive).SuppressCancellationThrow())
            {
                return;
            }

            boardManager.PlaceAllCardsToPosition();
            foreach (var _card in boardManager.visibleCards)
            {
                _ = _card.ShowFrontSide();
            }
        }
        
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();
            
            foreach (var _character in CharacterQuery.GetCharacters(false))
            {
                gameInfoRevealer.SetRevealLevelRpc(_character.ownerClientId.Value, 
                    nameof(CharacterInfoReveal.isRoleRevealed),
                    RevealLevel.Public, false);
            }
            
            gameManager.DoStateMethodRpc(GetType().FullName, nameof(GameEndingAnimation),
                new CustomRpcParams(CustomRpcParams.RpcTargetType.all));
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }

        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
            // Rejoin 02: the game is over, the session token has nothing left to open.
            Network.RejoinSessionStore.Clear();
        }
        
        public override void OnEndStateClient()
        {
            base.OnEndStateClient();
        }

        public override void StateUpdateServer()
        {
            base.StateUpdateServer();
        }
        
        public override void StateUpdateClient()
        {
            base.StateUpdateClient();
        }
    }
}