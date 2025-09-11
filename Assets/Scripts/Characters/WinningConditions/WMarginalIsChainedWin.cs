using System;
using GameLogic;

namespace Characters.WinningConditions
{
    [Serializable]
    public class WMarginalIsChainedWin : WinningCondition
    {
        public override WinningTeam GetWinningTeam()
        {
            return WinningTeam.marginal;
        }

        public override bool CheckCondition()
        {
            var _ownerCharacter = GameManager.instance.GetCharacter(ownerClientId);
            if (_ownerCharacter == null || _ownerCharacter.isFake)
            {
                return false;
            }

            return _ownerCharacter.isChained;
        }
    }
}