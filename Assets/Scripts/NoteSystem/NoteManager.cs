using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NoteSystem
{
    public class NoteManager : MonoBehaviour
    {
        public static NoteManager instance;
        
        // Key: PlayerID, Value: List of RoleIDs
        private Dictionary<ulong, List<Role>> possibleRolesByPlayer = new();
        private Dictionary<ulong, List<Role>> confirmedRolesByPlayer = new();
        private Dictionary<ulong, List<Role>> excludedRolesByPlayer = new();
        
        public event Action<ulong, List<Role>> onPossibleRolesByPlayerModified;
        public event Action<ulong, List<Role>> onConfirmedRolesByPlayerModified;
        public event Action<ulong, List<Role>> onExcludedRolesByPlayerModified;
        
        
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
        
        public List<Role> GetNotesForPlayer(ulong _playerID, NoteType _noteType)
        {
            return _noteType switch
            {
                NoteType.Possible => possibleRolesByPlayer.GetValueOrDefault(_playerID, new List<Role>()),
                NoteType.Confirmed => confirmedRolesByPlayer.GetValueOrDefault(_playerID, new List<Role>()),
                NoteType.Excluded => excludedRolesByPlayer.GetValueOrDefault(_playerID, new List<Role>()),
                _ => throw new ArgumentOutOfRangeException(nameof(_noteType), _noteType, null)
            };
        }
        
        public void AddNote(ulong _playerID, Role _role, NoteType _noteType)
        {
            Dictionary<ulong, List<Role>> _targetDictionary = _noteType switch
            {
                NoteType.Possible => possibleRolesByPlayer,
                NoteType.Confirmed => confirmedRolesByPlayer,
                NoteType.Excluded => excludedRolesByPlayer,
                _ => throw new ArgumentOutOfRangeException(nameof(_noteType), _noteType, null)
            };

            if (!_targetDictionary.ContainsKey(_playerID))
            {
                _targetDictionary[_playerID] = new List<Role>();
            }

            if (!_targetDictionary[_playerID].Any(_r => _r.IsTheSameRole(_role)))
            {
                _targetDictionary[_playerID].Add(_role);
            }
            
            switch (_noteType)
            {
                case NoteType.Possible:
                    onPossibleRolesByPlayerModified?.Invoke(_playerID, _targetDictionary[_playerID]);
                    break;
                case NoteType.Confirmed:
                    onConfirmedRolesByPlayerModified?.Invoke(_playerID, _targetDictionary[_playerID]);
                    break;
                case NoteType.Excluded:
                    onExcludedRolesByPlayerModified?.Invoke(_playerID, _targetDictionary[_playerID]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(_noteType), _noteType, null);
            }
        }
        
        public void RemoveNote(ulong _playerID, Role _role, NoteType _noteType)
        {
            Dictionary<ulong, List<Role>> _targetDictionary = _noteType switch
            {
                NoteType.Possible => possibleRolesByPlayer,
                NoteType.Confirmed => confirmedRolesByPlayer,
                NoteType.Excluded => excludedRolesByPlayer,
                _ => throw new ArgumentOutOfRangeException(nameof(_noteType), _noteType, null)
            };

            if (_targetDictionary.ContainsKey(_playerID) && _targetDictionary[_playerID].Any(_r => _r.IsTheSameRole(_role)))
            {
                _targetDictionary[_playerID].Remove(_role);
                
                switch (_noteType)
                {
                    case NoteType.Possible:
                        onPossibleRolesByPlayerModified?.Invoke(_playerID, _targetDictionary[_playerID]);
                        break;
                    case NoteType.Confirmed:
                        onConfirmedRolesByPlayerModified?.Invoke(_playerID, _targetDictionary[_playerID]);
                        break;
                    case NoteType.Excluded:
                        onExcludedRolesByPlayerModified?.Invoke(_playerID, _targetDictionary[_playerID]);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(_noteType), _noteType, null);
                }
            }
        }
    }
    
    public enum NoteType
    {
        Possible = 0,
        Confirmed = 1,
        Excluded = 2
    }
}