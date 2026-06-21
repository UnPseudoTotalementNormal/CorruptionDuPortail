using System.Collections.Generic;
using System.Linq;
using GameLogic;
using Unity.Netcode;

namespace Characters.Powers.Target
{
    public static class TargetUtils
    {
        public enum TargetType
        {
            Character,
            Role
        }
        
        public static bool IsTargetValid(Character _target, TargetIncludeFlags _includeFlags, TargetType _targetType = TargetType.Character)
        {
            return IsTargetValid(_target.ownerClientId.Value, _includeFlags, _targetType);
        }
        
        public static bool IsTargetValid(ulong _targetId, TargetIncludeFlags _includeFlags, TargetType _targetType = TargetType.Character)
        {
            List<ulong> _validTargets;
            if (_targetType == TargetType.Character)
            {
                _validTargets = GetTargetsForCharacters(_includeFlags);
            }
            else
            {
                _validTargets = GetTargetsForRoles(_includeFlags);
            }
            return _validTargets.Contains(_targetId);
        }
        
        public static List<ulong> GetTargetsForCharacters(TargetIncludeFlags _includeFlags)
        {
            List<Character> _targets = CharacterManager.instance.GetCharacters(false).ToList();

            if (!_includeFlags.HasFlag(TargetIncludeFlags.Self))
            {
                ulong _localClientId = CharacterManager.instance.GetLocalClientId();
                _targets.RemoveAll(_t => _t.ownerClientId.Value == _localClientId);
            }

            if (!_includeFlags.HasFlag(TargetIncludeFlags.Anomaly) ||
                !_includeFlags.HasFlag(TargetIncludeFlags.Marginal) ||
                !_includeFlags.HasFlag(TargetIncludeFlags.Chosen))
            {
                for (int _i = _targets.Count - 1; _i >= 0; _i--)
                {
                    CharacterInfoReveal _info =
                        CompositionRoot.For(NetworkManager.Singleton).GameInfoRevealer.GetCharacterInfo(_targets[_i].ownerClientId.Value);
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
                        CompositionRoot.For(NetworkManager.Singleton).GameInfoRevealer.GetCharacterInfo(_targets[_i].ownerClientId.Value);
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
            List<Character> _targets = CharacterManager.instance.GetCharacters(false).ToList();

            // Mirror GetTargetsForCharacters: when the power can't target Self, drop the caster so a
            // role only the caster holds is never offered. The role picker groups by roleID and keeps
            // the first non-excluded representative, so a role shared with another player still shows
            // (via that other player) while a role unique to the caster disappears.
            if (!_includeFlags.HasFlag(TargetIncludeFlags.Self))
            {
                ulong _localClientId = CharacterManager.instance.GetLocalClientId();
                _targets.RemoveAll(_t => _t.ownerClientId.Value == _localClientId);
            }

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