#region

using System;
using System.Collections.Generic;
using System.Linq;
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
            await BoardManager.instance.HideAllCards();
            
            foreach (var _winningTeam in winningTeams)
            {
                foreach (var _playerId in _winningTeam.Value)
                {
                    var _character = GameManager.instance.characterManager.GetCharacter(_playerId, false);
                    
                    var _newCard = BoardManager.instance.AddNewCard();
                    _newCard.SetInfo(_character);
                    _ = _newCard.ShowPseudoWithRevealedInfo();
                    _ = _newCard.ShowBackSide(true);
                }
            }
            
            await UniTask.Delay(TimeSpan.FromSeconds(1));

            BoardManager.instance.PlaceAllCardsToPosition();
            foreach (var _card in BoardManager.instance.visibleCards)
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
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();

            _ = GameEndingAnimation();
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