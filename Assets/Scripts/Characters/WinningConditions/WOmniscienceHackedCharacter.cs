using System;
using Characters.Powers;
using GameLogic;

namespace Characters.WinningConditions
{
    [Serializable]
    public class WOmniscienceHackedCharacter : WinningCondition
    {
        public override WinningTeam GetWinningTeam()
        {
            return WinningTeam.marginal;
        }

        public override bool CheckCondition()
        {
            var _ownerCharacter = GameManager.instance.GetCharacter(ownerClientId);
            POmniscience _omniscience = (POmniscience)_ownerCharacter.role.powers.Find(_p => _p.GetType() == typeof(POmniscience));
            if (_omniscience == null)
            {
                return false;
            }
            
            if (_omniscience.hackedCharacterClientId == POmniscience.HACKED_CHARACTER_DEFAULT)
            {
                return false;
            }
            
            var _hackedCharacter = GameManager.instance.GetCharacter(_omniscience.hackedCharacterClientId);
            if (_hackedCharacter == null)
            {
                return false;
            }

            return _hackedCharacter.isChained && _hackedCharacter.role.factionType == FactionType.chosen;
        }
    }
}