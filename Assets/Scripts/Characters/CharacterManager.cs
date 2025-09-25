using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace Characters
{
    public class CharacterManager : NetworkBehaviour
    {
        public static CharacterManager instance;

        [field: SerializeField] private List<Character> _characters = new();
        
        public event Action<List<Character>> onCharactersListUpdated;
        
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this.gameObject);
            }
            else
            {
                instance = this;
            }
        }
        
        public Character GetLocalCharacter(bool _triggerUpdate = true)
        {
            if (_triggerUpdate)
            {
                StartCoroutine(TriggerOnCharactersListUpdatedAtEndOfFrame());
            }
            return _characters.FirstOrDefault(_character => _character.ownerClientId == NetworkManager.LocalClientId);
        }

        public Character GetCharacter(ulong _characterId, bool _triggerUpdate = true)
        {
            return GetCharacters(_triggerUpdate).FirstOrDefault(_c => _c.ownerClientId == _characterId);
        }

        public List<Character> GetCharacters(bool _triggerUpdate = true)
        {
            if (_triggerUpdate)
            {
                StartCoroutine(TriggerOnCharactersListUpdatedAtEndOfFrame());
            }
            return _characters;
        }
        
        #region Characters Updates

        [Rpc(SendTo.Server)]
        public void AskForUpdateAllCharactersRpc()
        {
            if (!IsServer)
            {
                return;
            }
     
            UpdateAllCharactersRpc(GetCharacters().ToArray());
        }
    
        [Rpc(SendTo.NotServer)]
        private void UpdateAllCharactersRpc(Character[] _characters)
        {
            foreach (var _character in _characters)
            {
                var _sameCharacter = GetCharacters().FirstOrDefault(_c => _c.ownerClientId == _character.ownerClientId);
                if (_sameCharacter != null)
                {
                    _sameCharacter.UpdateCharacter(_character);
                }
                else
                {
                    this._characters.Add(_character);
                }
            }
            onCharactersListUpdated?.Invoke(this._characters);
        }
        
        public IEnumerator TriggerOnCharactersListUpdatedAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            onCharactersListUpdated?.Invoke(_characters);
        }

        #endregion
        
        public Character CreateNewFakeCharacter()
        {
            Character _character = new Character();
            _character.ownerClientId = GameValues.FAKE_CLIENT_ID - (ulong)instance.GetCharacters().Count(_c => _c.isFake);
            _characters.Add(_character);
            return _character;
        }
    }
}