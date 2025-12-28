using System.Collections.Generic;
using System.Linq;
using GameLogic;
using Unity.Netcode;

namespace Characters.Powers.Target
{
    public static class TargetUtils
    {
        public static bool IsTargetValid(Character _target, TargetIncludeFlags _includeFlags)
        {
            return IsTargetValid(_target.ownerClientId.Value, _includeFlags);
        }
        
        public static bool IsTargetValid(ulong _targetId, TargetIncludeFlags _includeFlags)
        {
            List<ulong> _validTargets = GetTargetsForCharacters(_includeFlags);
            return _validTargets.Contains(_targetId);
        }
        
        public static List<ulong> GetTargetsForCharacters(TargetIncludeFlags _includeFlags)
        {
            List<Character> _targets = GameManager.instance.characterManager.GetCharacters(false).ToList();

            if (!_includeFlags.HasFlag(TargetIncludeFlags.Self))
            {
                ulong _localClientId = NetworkManager.Singleton.LocalClientId;
                _targets.RemoveAll(_t => _t.ownerClientId.Value == _localClientId);
            }

            if (!_includeFlags.HasFlag(TargetIncludeFlags.Anomaly) ||
                !_includeFlags.HasFlag(TargetIncludeFlags.Marginal) ||
                !_includeFlags.HasFlag(TargetIncludeFlags.Chosen))
            {
                for (int _i = _targets.Count - 1; _i >= 0; _i--)
                {
                    CharacterInfoReveal _info =
                        GameManager.instance.gameInfoRevealer.GetCharacterInfo(_targets[_i].ownerClientId.Value);
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
                for (int _i = _targets.Count - 1; _i >= 0; _i--)
                {
                    CharacterInfoReveal _info =
                        GameManager.instance.gameInfoRevealer.GetCharacterInfo(_targets[_i].ownerClientId.Value);
                    if (_info.isCorruptRevealed > RevealLevel.False && _targets[_i].isCorrupted.Value)
                    {
                        _targets.RemoveAt(_i);
                    }
                }
            }

            if (!_includeFlags.HasFlag(TargetIncludeFlags.Blessed))
            {
                _targets.RemoveAll(_t => _t.isBlessed.Value);
            }
            
            if (!_includeFlags.HasFlag(TargetIncludeFlags.Chained))
            {
                _targets.RemoveAll(_t => _t.isChained.Value);
            }

            if (!_includeFlags.HasFlag(TargetIncludeFlags.Healed))
            {
                _targets.RemoveAll(_t => _t.isHealed.Value);
            }

            return _targets.Select(_t => _t.ownerClientId.Value).ToList();
        }

        public static List<ulong> GetTargetsForRoles(TargetIncludeFlags _includeFlags)
        {
            List<Character> _targets = GameManager.instance.characterManager.GetCharacters(false).ToList();

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
            
            return _targets.Select(_t => _t.ownerClientId.Value).ToList();
        }
    }
}