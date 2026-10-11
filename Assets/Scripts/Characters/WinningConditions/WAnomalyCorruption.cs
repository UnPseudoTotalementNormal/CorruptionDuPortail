#region

using System;
using System.Linq;
using CorruptionDuPortail.Domain;
using GameLogic;

#endregion

namespace Characters.WinningConditions
{
    [Serializable]
    public class WAnomalyCorruption : WinningCondition
    {
        public override string Description => "Les Anomalies gagnent quand tous les joueurs sont corrompus.";

        public override WinningTeam GetWinningTeam()
        {
            return WinningTeam.anomaly;
        }

        public override bool CheckCondition()
        {
            foreach (var _character in CharacterManager.instance.GetCharacters(false).Where(_c => !_c.isFake))
            {
                if (!_character.isCorrupted.Value)
                {
                    return false;
                }
            }
            return true;
        }

        // Story 2.4 — snapshot-based equivalent of the pull above. Reads exactly: IsFake (filter), IsCorrupted.
        // First non-fake non-corrupted character → false; vacuously true over an empty non-fake population.
        public override bool CheckCondition(GameSnapshot snapshot)
        {
            foreach (var _character in snapshot.Characters)
            {
                if (_character.IsFake)
                {
                    continue;
                }

                if (!_character.IsCorrupted)
                {
                    return false;
                }
            }
            return true;
        }
    }
}