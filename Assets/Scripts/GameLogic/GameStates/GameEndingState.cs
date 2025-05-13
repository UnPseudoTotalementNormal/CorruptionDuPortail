using System.Collections.Generic;
using System.Linq;
using Characters.WinningConditions;
using Network;
using UnityEngine;

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
            foreach (var _winningTeam in winningTeams)
            {
                Debug.Log("Winning team: " + _winningTeam.Key);
                foreach (var _playerId in _winningTeam.Value)
                {
                    Debug.Log("Player ID: " + _playerId);
                }
            }
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