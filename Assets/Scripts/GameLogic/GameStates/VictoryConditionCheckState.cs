#region

using System.Collections.Generic;
using System.Linq;
using Characters.WinningConditions;
using UnityEngine;

#endregion

namespace GameLogic.GameStates
{
    [CreateAssetMenu(fileName = "VictoryConditionCheckState", menuName = "GameStates/VictoryConditionCheckState")]
    public class VictoryConditionCheckState : GameState
    {
        public override void OnStateCreated()
        { 
            base.OnStateCreated();
        }

        public override void OnStartStateServer()
        {
            base.OnStartStateServer();

            Dictionary<WinningTeam, HashSet<ulong>> _winningTeams = new();
                
            foreach (var _currentCharacters in gameManager.characterManager.GetCharacters(false))
            {
                foreach (var _currentWinningCondition in _currentCharacters.role.winningConditions)
                {
                    if (_currentWinningCondition.CheckCondition())
                    {
                        if (!_winningTeams.ContainsKey(_currentWinningCondition.GetWinningTeam()))
                        {
                            _winningTeams[_currentWinningCondition.GetWinningTeam()] = new HashSet<ulong>();
                        }
                        
                        _winningTeams[_currentWinningCondition.GetWinningTeam()].Add(_currentCharacters.ownerClientId.Value);
                    }
                }
            }

            if (_winningTeams.Count == 0)
            {
                gameManager.NextGameState();
                return;
            }

            var _gameEndingState = (GameEndingState)gameManager.GetGameStates(typeof(GameEndingState)).First();
            _gameEndingState.SetWinnersServer(_winningTeams);
            
            gameManager.NextGameState(true);
        }

        public override void OnEndStateServer()
        {
            base.OnEndStateServer();
        }
        
        public override void OnStartStateClient()
        {
            base.OnStartStateClient();
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