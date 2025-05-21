#region

using System;
using System.Linq;
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
            var _characters = GameManager.instance.GetCharacters(false).Where(_c => !_c.isFake).ToList();
            
            if (!_characters.Exists(_c => _c.role.factionType == FactionType.anomaly))
            {
                return false;
            }
            
            foreach (var _character in _characters)
            {
                if (_character.role.factionType != FactionType.anomaly)
                {
                    continue;
                }
                
                if (!_character.isChained)
                {
                    return false;
                }
            }
            return true;
        }
    }
}