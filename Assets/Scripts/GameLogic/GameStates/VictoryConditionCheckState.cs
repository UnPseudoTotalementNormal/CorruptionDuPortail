#region

using System.Collections.Generic;
using System.Linq;
using Characters.WinningConditions;
using CorruptionDuPortail.Domain;
using GameLogic.Snapshot;
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

            // Story 2.7 — evaluate off an immutable snapshot built once, synchronously, before any await.
            GameSnapshot _snapshot = GameSnapshotBuilder.FromLiveState(gameManager);

            // Story 2.8 — the win-team aggregation is a pure Domain POCO (VictoryEvaluator). The adapter only maps
            // live state in (fakes filtered at the source) and applies the returned decision (NFR4 — no transition
            // inside the POCO).
            var _owners = new List<ConditionsForOwner>();
            foreach (var _currentCharacters in CharacterQuery.GetCharacters(false))
            {
                if (_currentCharacters.isFake)
                {
                    continue;
                }

                _owners.Add(new ConditionsForOwner(_currentCharacters.ownerClientId.Value, _currentCharacters.role.winningConditions));
            }

            Dictionary<WinningTeam, HashSet<ulong>> _winningTeams = new VictoryEvaluator().Evaluate(_snapshot, _owners);

            if (_winningTeams.Count == 0)
            {
                Loop.NextGameState();
                return;
            }

            var _gameEndingState = (GameEndingState)gameManager.GetGameStates(typeof(GameEndingState)).First();
            _gameEndingState.SetWinnersServer(_winningTeams);
            
            Loop.SetGameState(typeof(GameEndingState));
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