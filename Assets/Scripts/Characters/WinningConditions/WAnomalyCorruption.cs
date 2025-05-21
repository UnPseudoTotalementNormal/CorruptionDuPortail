#region

using System;
using System.Linq;
using GameLogic;

#endregion

namespace Characters.WinningConditions
{
    [Serializable]
    public class WAnomalyCorruption : WinningCondition
    {
        public override WinningTeam GetWinningTeam()
        {
            return WinningTeam.anomaly;
        }

        public override bool CheckCondition()
        {
            foreach (var _character in GameManager.instance.GetCharacters(false).Where(_c => !_c.isFake))
            {
                if (!_character.isCorrupted)
                {
                    return false;
                }
            }
            return true;
        }
    }
}