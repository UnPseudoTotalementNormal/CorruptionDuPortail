using System;
using CorruptionDuPortail.Domain;
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
            var _ownerCharacter = CharacterManager.instance.GetCharacter(ownerClientId);
            if (_ownerCharacter == null || _ownerCharacter.isFake)
            {
                return false;
            }

            return _ownerCharacter.isChained.Value;
        }

        // Story 2.3 — snapshot-based equivalent of the pull above. Owner absent (≡ GetCharacter == null) or fake → false;
        // otherwise the owner's chained state. Reads exactly: OwnerClientId (match key), IsFake, IsChained.
        public override bool CheckCondition(GameSnapshot snapshot)
        {
            foreach (var _character in snapshot.Characters)
            {
                if (_character.OwnerClientId == ownerClientId)
                {
                    return !_character.IsFake && _character.IsChained;
                }
            }

            return false;
        }
    }
}