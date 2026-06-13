#region

using System;
using System.Linq;
using CorruptionDuPortail.Domain;
using GameLogic;

#endregion

namespace Characters.WinningConditions
{
    [Serializable]
    public class WChosenChainedAllAnomaly : WinningCondition
    {
        public override WinningTeam GetWinningTeam()
        {
            return WinningTeam.chosen;
        }
        
        public override bool CheckCondition()
        {
            var _characters = CharacterManager.instance.GetCharacters(false).Where(_c => !_c.isFake).ToList();
            
            foreach (var _character in _characters)
            {
                if (_character.role.factionType != FactionType.anomaly)
                {
                    continue;
                }

                if (!_character.isChained.Value)
                {
                    return false;
                }
            }
            return true;
        }

        // Story 2.5 — snapshot-based equivalent of the pull above. Reads exactly: IsFake (filter), FactionType, IsChained.
        // Non-anomaly → continue; an anomaly not chained → false; vacuously true with zero anomalies.
        public override bool CheckCondition(GameSnapshot snapshot)
        {
            foreach (var _character in snapshot.Characters)
            {
                if (_character.IsFake)
                {
                    continue;
                }

                if (_character.FactionType != FactionType.anomaly)
                {
                    continue;
                }

                if (!_character.IsChained)
                {
                    return false;
                }
            }
            return true;
        }
    }
}