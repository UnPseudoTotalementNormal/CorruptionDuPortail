using System.Collections.Generic;
using System.Linq;
using GameLogic;
using Unity.Netcode;

namespace Characters.Powers.Target
{
    public class TargetUtils
    {
        public List<ulong> GetTargetsForCharacters(TargetIncludeFlags _includeFlags)
        {
            List<Character> _targets = GameManager.instance.GetCharacters(false);

            if (!_includeFlags.HasFlag(TargetIncludeFlags.Self))
            {
                ulong _localClientId = NetworkManager.Singleton.LocalClientId;
                _targets.RemoveAll(_t => _t.ownerClientId == _localClientId);
            }

            if (!_includeFlags.HasFlag(TargetIncludeFlags.Anomaly) ||
                !_includeFlags.HasFlag(TargetIncludeFlags.Marginal) ||
                !_includeFlags.HasFlag(TargetIncludeFlags.Chosen))
            {
                for (int _i = _targets.Count - 1; _i >= 0; _i--)
                {
                    CharacterInfoReveal _info =
                        GameManager.instance.gameInfoRevealer.GetCharacterInfo(_targets[_i].ownerClientId);
                    if (_info.isRoleRevealed > RevealLevel.False)
                    {
                        FactionType _faction = _targets[_i].role.factionType;
                        if ((_faction == FactionType.anomaly && !_includeFlags.HasFlag(TargetIncludeFlags.Anomaly)) ||
                            (_faction == FactionType.marginal && !_includeFlags.HasFlag(TargetIncludeFlags.Marginal)) ||
                            (_faction == FactionType.chosen && !_includeFlags.HasFlag(TargetIncludeFlags.Chosen)))
                        {
                            _targets.RemoveAt(_i);
                        }
                    }
                }
            }

            if (!_includeFlags.HasFlag(TargetIncludeFlags.Corrupted))
            {
                _targets.RemoveAll(_t => _t.isCorrupted);
            }

            if (!_includeFlags.HasFlag(TargetIncludeFlags.Blessed))
            {
                _targets.RemoveAll(_t => _t.isBlessed);
            }
            
            if (!_includeFlags.HasFlag(TargetIncludeFlags.Chained))
            {
                _targets.RemoveAll(_t => _t.isChained);
            }

            return _targets.Select(_t => _t.ownerClientId).ToList();
        }

        public List<ulong> GetTargetsForRoles(TargetIncludeFlags _includeFlags)
        {
            List<Character> _targets = GameManager.instance.GetCharacters(false);

            if (!_includeFlags.HasFlag(TargetIncludeFlags.Fake))
            {
                //TODO: only if player knows about fake characters
                /*foreach (var _t in _targets.ToList().Where(_t => _t.isFake))
                {
                    _targets.Remove(_t);
                }*/
            }
            
            if (!_includeFlags.HasFlag(TargetIncludeFlags.Anomaly) ||
                !_includeFlags.HasFlag(TargetIncludeFlags.Marginal) ||
                !_includeFlags.HasFlag(TargetIncludeFlags.Chosen))
            {
                for (int _i = _targets.Count - 1; _i >= 0; _i--)
                {
                    FactionType _faction = _targets[_i].role.factionType;
                    if ((_faction == FactionType.anomaly && !_includeFlags.HasFlag(TargetIncludeFlags.Anomaly)) ||
                        (_faction == FactionType.marginal && !_includeFlags.HasFlag(TargetIncludeFlags.Marginal)) ||
                        (_faction == FactionType.chosen && !_includeFlags.HasFlag(TargetIncludeFlags.Chosen)))
                    {
                        _targets.RemoveAt(_i);
                    }
                }
            }
            
            return _targets.Select(_t => _t.ownerClientId).ToList();
        }
    }
}