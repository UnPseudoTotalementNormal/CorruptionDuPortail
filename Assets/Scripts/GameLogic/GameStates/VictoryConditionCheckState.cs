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

            // Preserve the exact original flow: resolve victory now; if there is no winner, advance the loop.
            if (TryResolveVictoryNow())
            {
                return;
            }

            Loop.NextGameState();
        }

        /// <summary>
        /// [LEAVE] Phase 2 (epic-player-leave-stability) — reusable, out-of-band victory evaluation. Builds the
        /// immutable snapshot, runs the pure <see cref="VictoryEvaluator"/>, and applies the decision: if any team
        /// has won it pushes the winners into <see cref="GameEndingState"/>, jumps there, and returns <c>true</c>;
        /// otherwise it returns <c>false</c> and performs NO transition (so the caller can decide what to do next).
        /// Extracted from <see cref="OnStartStateServer"/> so the leave pipeline can request an immediate re-check
        /// after chaining a leaver (last-anomaly → chosen win) WITHOUT blindly forcing this state from an arbitrary
        /// mid-state (which would disrupt the loop). Server-only.
        /// </summary>
        public bool TryResolveVictoryNow()
        {
            if (!gameManager.IsServer)
            {
                Debug.LogError("TryResolveVictoryNow can only be called on the server");
                return false;
            }

            // Story 2.7 — evaluate off an immutable snapshot built once, synchronously, before any await.
            GameSnapshot _snapshot = GameSnapshotBuilder.FromLiveState(gameManager);

            // Story 2.8 — the win-team aggregation is a pure Domain POCO (VictoryEvaluator). The adapter only maps
            // live state in (fakes filtered at the source) and applies the returned decision (NFR4 — no transition
            // inside the POCO).
            var _owners = new List<ConditionsForOwner>();
            foreach (var _currentCharacters in CharacterQuery.GetCharacters(false))
            {
                // A fake seat — or one that never got a role (e.g. a just-removed leaver observed out-of-band) —
                // carries no winning conditions; skipping it also null-guards the role read below.
                if (_currentCharacters.isFake || _currentCharacters.role == null)
                {
                    continue;
                }

                _owners.Add(new ConditionsForOwner(_currentCharacters.ownerClientId.Value, _currentCharacters.role.winningConditions));
            }

            Dictionary<WinningTeam, HashSet<ulong>> _winningTeams = new VictoryEvaluator().Evaluate(_snapshot, _owners);

            if (_winningTeams.Count == 0)
            {
                return false;
            }

            var _gameEndingState = (GameEndingState)gameManager.GetGameStates(typeof(GameEndingState)).First();
            _gameEndingState.SetWinnersServer(_winningTeams);

            Loop.SetGameState(typeof(GameEndingState));
            return true;
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