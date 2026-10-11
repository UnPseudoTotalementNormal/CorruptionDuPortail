using System;
using Characters.Powers;
using CorruptionDuPortail.Domain;
using GameLogic;

namespace Characters.WinningConditions
{
    // TODO(robot-victory): refondre le système de victoire du Robot. Le modèle actuel ("le Robot est
    // techniquement auto-piraté", sa victoire = la cible piratée chaînée + chosen) est jugé bancal par
    // Poyo. À reprendre proprement (cf. glitch auto-hack from-start dans CardHackGlitch, qui ne fait
    // qu'illustrer visuellement ce modèle sans le corriger). Ne pas modifier cette condition sans
    // valider la refonte avec Poyo (design-owned).
    [Serializable]
    public class WOmniscienceHackedCharacter : WinningCondition
    {
        public override string Description => "Il gagne si l'élu qu'il a piraté se fait enchaîner.";

        public override WinningTeam GetWinningTeam()
        {
            return WinningTeam.marginal;
        }

        public override bool CheckCondition()
        {
            var _ownerCharacter = CharacterManager.instance.GetCharacter(ownerClientId);
            POmniscience _omniscience = (POmniscience)_ownerCharacter.role.powers.Find(_p => _p.GetType() == typeof(POmniscience));
            if (_omniscience == null)
            {
                return false;
            }
            
            if (_omniscience.hackedCharacterClientId == POmniscience.HACKED_CHARACTER_DEFAULT)
            {
                return false;
            }
            
            var _hackedCharacter = CharacterManager.instance.GetCharacter(_omniscience.hackedCharacterClientId);
            if (_hackedCharacter == null)
            {
                return false;
            }

            return _hackedCharacter.isChained.Value && _hackedCharacter.role.factionType == FactionType.chosen;
        }

        // Story 2.6 — snapshot-based equivalent of the pull above. Mirrors the 4-way conjunction AND the pull's
        // no-owner-guard NRE (golden O1: owner not found → NullReferenceException). The single hackedId == DEFAULT
        // guard is verdict-equivalent to both pull early-returns (no POmniscience, and POmniscience-but-default),
        // since the Story 2.1 builder maps both to HACKED_CHARACTER_DEFAULT.
        public override bool CheckCondition(GameSnapshot snapshot)
        {
            CharacterSnapshot _owner = null;
            foreach (var _c in snapshot.Characters)
            {
                if (_c.OwnerClientId == ownerClientId)
                {
                    _owner = _c;
                    break;
                }
            }

            ulong _hackedId = _owner.HackedByOmniscienceTarget; // NO null guard — mirrors the pull's owner.role.powers NRE
            if (_hackedId == POmniscience.HACKED_CHARACTER_DEFAULT)
            {
                return false;
            }

            foreach (var _c in snapshot.Characters)
            {
                if (_c.OwnerClientId == _hackedId)
                {
                    return _c.IsChained && _c.FactionType == FactionType.chosen;
                }
            }

            return false; // hacked target not found
        }
    }
}