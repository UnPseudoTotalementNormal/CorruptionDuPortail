using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Characters.Powers;
using Cysharp.Threading.Tasks;
using GameLogic;
using Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using Random = UnityEngine.Random;

namespace Characters
{
    public class CharacterManager : NetworkBehaviour
    {
        public static CharacterManager instance;
        
        private Dictionary<ulong, UniTaskCompletionSource<Character>> _spawnPromises = new();

        public async UniTask<Character> GetCharacterAsync(ulong _clientId)
        {
            var _character = GetCharacter(_clientId, false);
            if (_character != null) return _character;

            if (!_spawnPromises.ContainsKey(_clientId))
            {
                _spawnPromises[_clientId] = new UniTaskCompletionSource<Character>();
            }

            return await _spawnPromises[_clientId].Task;
        }

        public void RegisterSpawnedCharacter(Character _character)
        {
            ulong _id = _character.ownerClientId.Value;
            if (_spawnPromises.TryGetValue(_id, out var _promise))
            {
                _promise.TrySetResult(_character);
                _spawnPromises.Remove(_id);
            }
        }
        
        public RpcParams GetSafeRpcTarget(ulong _clientId)
        {
            var _target = _clientId >= 100 
                ? NetworkManager.RpcTarget.Single(0, RpcTargetUse.Persistent) 
                : NetworkManager.RpcTarget.Single(_clientId, RpcTargetUse.Persistent);
                
            return new RpcParams { Send = new RpcSendParams { Target = _target } };
        }

        [SerializeField] private Transform _charactersParent;
        [SerializeField] private NetworkObject _characterPrefab;

        // Backing cache for the resolved characters. Rebuilt only when
        // networkedCharacters actually changes (OnListChanged) or on spawn, so
        // the very frequent reads below allocate nothing per access.
        private readonly List<Character> _charactersCache = new();

        // Set to true when the last rebuild could not resolve every
        // NetworkBehaviourReference (network spawn still in flight). While dirty,
        // reads will retry the rebuild so a late-resolving Character eventually
        // appears, preserving the original per-access TryGet tolerance without
        // paying its cost in steady state.
        private bool _cacheDirty = true;

        private List<Character> _characters
        {
            get
            {
                if (_cacheDirty)
                {
                    RebuildCharactersCache();
                }
                return _charactersCache;
            }
        }

        private void RebuildCharactersCache()
        {
            _charactersCache.Clear();
            bool _allResolved = true;
            foreach (var _networkBehaviourReference in networkedCharacters)
            {
                if (_networkBehaviourReference.TryGet(out Character _character))
                {
                    _charactersCache.Add(_character);
                }
                else
                {
                    // Character network object not resolvable yet (spawn in
                    // flight): skip it (no crash) and keep the cache dirty so a
                    // later read picks it up.
                    _allResolved = false;
                }
            }
            _cacheDirty = !_allResolved;
        }

        private void OnNetworkedCharactersChanged(NetworkListEvent<NetworkBehaviourReference> _changeEvent)
        {
            // Authoritative source changed: force a rebuild on next access.
            _cacheDirty = true;
        }

        private NetworkList<NetworkBehaviourReference> networkedCharacters = new();
        
        public event Action<List<Character>> onCharactersListUpdated;
        public event Action onLocalIdentityChanged;

        private ulong? _debugPossessedId = null;
        
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            instance = this;
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Subscribe to the authoritative list so the cache is invalidated
            // whenever it changes.
            networkedCharacters.OnListChanged += OnNetworkedCharactersChanged;

            // The NetworkList is delivered already populated to late joiners
            // without raising OnListChanged for the initial state, so force a
            // rebuild here (kept dirty until every reference resolves).
            _cacheDirty = true;
        }

        public override void OnNetworkDespawn()
        {
            networkedCharacters.OnListChanged -= OnNetworkedCharactersChanged;

            if (instance == this)
            {
                instance = null;
            }

            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            // Safety net: the duplicate singleton instance is destroyed in Awake
            // and may never spawn/despawn; also covers teardown ordering where
            // OnNetworkDespawn was not invoked. Unsubscribing twice is harmless.
            networkedCharacters.OnListChanged -= OnNetworkedCharactersChanged;

            if (instance == this)
            {
                instance = null;
            }
        }

        public ulong GetLocalClientId() => _debugPossessedId ?? NetworkManager.LocalClientId;
        
        public bool IsLocalOrSimulated(ulong _clientId)
        {
            if (_clientId == GetLocalClientId()) return true;
            if (_clientId >= 100 && IsServer) return true;
            return false;
        }

        public Character GetLocalCharacter(bool _triggerUpdate = true)
        {
            if (_triggerUpdate)
            {
                StartCoroutine(TriggerOnCharactersListUpdatedAtEndOfFrame());
            }
            return _characters.FirstOrDefault(_character => _character.ownerClientId.Value == GetLocalClientId());
        }

        public Character GetCharacter(ulong _characterId, bool _triggerUpdate = true)
        {
            return GetCharacters(_triggerUpdate).FirstOrDefault(_c => _c.ownerClientId.Value == _characterId);
        }

        public List<Character> GetCharacters(bool _triggerUpdate = true)
        {
            if (_triggerUpdate)
            {
                StartCoroutine(TriggerOnCharactersListUpdatedAtEndOfFrame());
            }
            // Return a defensive copy: the previous implementation handed back a
            // freshly built list on every call, so external callers that mutate
            // the result (e.g. RoleAttributionState calls GetCharacters().Add(..))
            // never affected the real state. Preserve that exact behaviour and
            // protect the backing cache from external mutation. The expensive
            // TryGet iteration is gone; this only copies already-resolved refs.
            return new List<Character>(_characters);
        }
        
        [Rpc(SendTo.Everyone, RequireOwnership = true)]
        public void GiveRoleToCharacterRpc(ulong _characterId, Role _role)
        {
            _ = GiveRoleToCharacterAsync(_characterId, _role);
        }

        private async UniTaskVoid GiveRoleToCharacterAsync(ulong _characterId, Role _role)
        {
            Character _character = await GetCharacterAsync(_characterId);

            _character.role = _role;
            _character.UpdateRoleRpc(_role);
            _character.CheckForPowersRpc();
            _character.role.ownerClientId = _characterId;
        }
        
        #region Characters Updates

        [Rpc(SendTo.Server)]
        public void AskForUpdateAllCharactersRpc()
        {
            if (!IsServer)
            {
                return;
            }
     
            foreach (var _character in _characters)
            {
                _character.AskForRoleUpdateRpc();
            }
            
            UpdateAllCharactersRpc();
        }
    
        [Rpc(SendTo.NotServer)]
        private void UpdateAllCharactersRpc()
        {
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
            ulong _newFakeClientId = GameValues.FAKE_CLIENT_ID - (ulong)instance.GetCharacters().Count(_c => _c.isFake);
            return AddNewCharacter(_newFakeClientId);
        }

        public void SpawnSimulatedPlayer()
        {
            Assert.IsTrue(IsServer, "SpawnSimulatedPlayer can only be called on server");
            
            // Debug range starting at 100
            ulong _debugId = 100 + (ulong)instance.GetCharacters().Count(_c => !_c.isFake && _c.ownerClientId.Value >= 100);
            
            // Add to LobbyPlayerInfoHolder first so name/info is available
            LobbyPlayerInfoHolder.instance.AddDebugPlayer(_debugId, $"Simulated {_debugId - 99}");
            
            AddNewCharacter(_debugId);
        }

        public void SetPossessedIdentity(ulong? _id)
        {
            _debugPossessedId = _id;
            onLocalIdentityChanged?.Invoke();
        }

        public Character AddNewCharacter(ulong _clientId) //todo: create all characters on start, and simply change ownerID when starting the game, to not have spawn issues
        {
            if (_characters.Any(_c => _c.ownerClientId.Value == _clientId))
            {
                return null;
            }
            
            NetworkObject _newCharacterObject = NetworkManager.SpawnManager.InstantiateAndSpawn(_characterPrefab, destroyWithScene: true);
            
            if (!_charactersParent.GetComponent<NetworkObject>().IsSpawned)
            {
                StartCoroutine(WaitForParentToSpawnAndSet(_newCharacterObject, _charactersParent.GetComponent<NetworkObject>()));
            }
            else
            {
                _newCharacterObject.TrySetParent(_charactersParent, false);
            }
            
            Character _newCharacter = _newCharacterObject.GetComponent<Character>();
            _newCharacter.ownerClientId.Value = _clientId;

            // Authoritative source. Adding here raises OnListChanged on the
            // server and invalidates the cache; the previous _characters.Add(..)
            // on the throwaway list was a silent no-op.
            networkedCharacters.Add(_newCharacter);

            // Force the cache to include the just-spawned character (its
            // NetworkObject is already spawned locally so TryGet resolves) and
            // notify listeners with the up-to-date list, as before.
            _cacheDirty = true;
            onCharactersListUpdated?.Invoke(_characters);
            return _newCharacter;
        }

        public void RemoveCharacter(ulong _clientId)
        {
            Character _characterToRemove = _characters.FirstOrDefault(_c => _c.ownerClientId.Value == _clientId);
            if (_characterToRemove != null)
            {
                // Remove from the authoritative source BEFORE despawning: once
                // the NetworkObject is despawned its NetworkBehaviourReference no
                // longer resolves, so we must match the entry while it is still
                // valid. The previous _characters.Remove(..) on the throwaway
                // list was a silent no-op.
                for (int _i = networkedCharacters.Count - 1; _i >= 0; _i--)
                {
                    if (networkedCharacters[_i].TryGet(out Character _c) && _c == _characterToRemove)
                    {
                        networkedCharacters.RemoveAt(_i);
                    }
                }

                var _networkObject = _characterToRemove.GetComponent<NetworkObject>();
                if (_networkObject != null && _networkObject.IsSpawned)
                {
                    _networkObject.Despawn();
                }
            }
            // Removal raises OnListChanged on the server; also force it for the
            // immediate host-side read, then notify with the up-to-date list.
            _cacheDirty = true;
            onCharactersListUpdated?.Invoke(_characters);
        }
        
        public void GivePowerToCharacter(ulong _characterId, Power _power)
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "GivePowerToCharacter should only be called on the server");
            Character _character = GetCharacter(_characterId);
            Assert.IsNotNull(_character, $"Character with id {_characterId} not found when trying to give power {_power.powerName}");
            
            Power _newPower = Instantiate(_power, null);
            NetworkObject _powerNetworkObject = _newPower.GetComponent<NetworkObject>();
            _powerNetworkObject.GetComponent<Power>().idHolderServer = _characterId;
            _powerNetworkObject.Spawn(true);
            StartCoroutine(
                WaitForParentToSpawnAndSet(_powerNetworkObject, _character.GetComponent<NetworkObject>(), 
                    (_result) => { OnPowerReparentComplete(_newPower, _result); })
                );
        }
        
        public void RemovePowerFromCharacter(ulong _characterId, Power _power)
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "RemovePowerFromCharacter should only be called on the server");
            Character _character = GetCharacter(_characterId);
            Assert.IsNotNull(_character, $"Character with id {_characterId} not found when trying to remove power {_power.powerName}");
            
            if (_power.ownerCharacter != _character)
            {
                Debug.LogError($"Power {_power.powerName} does not belong to character {_characterId}");
                return;
            }
            
            PowerManager.instance.RemovePowerFromCharacterPowerListRpc(_characterId, new(_power));
            NetworkObject _powerNetworkObject = _power.GetComponent<NetworkObject>();
            if (_powerNetworkObject != null)
            {
                _powerNetworkObject.Despawn();
            }
        }
        
        

        private void OnPowerReparentComplete(Power _power, bool _result)
        {
            if (!_result)
            {
                Debug.LogError("Failed to reparent power " + _power.powerName + " to character " + _power.ownerClientId);
                return;
            }

            PowerManager.instance.OnPowerReparentedServer(_power);
        }
        
        private IEnumerator WaitForParentToSpawnAndSet(NetworkObject _child, NetworkObject _parent, Action<bool> _callback = null)
        {
            if (!_child || !_parent)
            {
                Debug.LogError("Child or parent is null in WaitForParentToSpawnAndSet");
                yield break;
            }
            
            while (!_parent.IsSpawned || !_child.IsSpawned)
            {
                yield return null;
            }

            if (!_child.TrySetParent(_parent.transform, false))
            {
                Debug.LogError("Failed to set parent in WaitForParentToSpawnAndSet");
                _callback?.Invoke(false);
                yield break;
            }

            _callback?.Invoke(true);
        }
    }
}