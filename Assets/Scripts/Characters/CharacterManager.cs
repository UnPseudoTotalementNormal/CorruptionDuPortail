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
    // Story 9.1 (Epic 9 / D3): CharacterManager implements the read slice ICharacterQuery. All six
    // members are already public, so this is satisfied implicitly — zero behaviour change.
    // Story 9.2 (Epic 9 / D3): also implements the command slice ICharacterCommand (spawn/mutation),
    // again implicit (every command member is already public). GetSafeRpcTarget / IsLocalOrSimulated
    // stay OFF both interfaces — they are NFR5 network-authority internals (§3(d)).
    public class CharacterManager : NetworkBehaviour, ICharacterQuery, ICharacterCommand
    {
        // Story 9.3 (Epic 9 / D3): the global façade is NARROWED to a recorded-callers-only surface. 9.1/9.2
        // injected every gameplay read/command consumer onto ICharacterQuery / ICharacterCommand (resolved via
        // CompositionRoot); no gameplay path reaches `instance` anymore. The remaining callers are all
        // verify-don't-force exceptions WITH a death date — see the recorded leftovers census in
        // refactor-architecture-despaghetti.md §4. Story 12.2 rerouted the bulk of the UI leaves off this façade:
        //   lane A (scene, [SerializeField]): PowersBar / AnonymeMessageButton / InfoTableSystem / CardPickerManager / TooltipLinkParser;
        //   prefab push (slice from parent/host): MeIconCard / NoteRibbon / NoteChoosePanel / VoteStateUI / AwakeningRecapCorruption.
        // STILL on the façade → Epic 12.3 (the final sweep): RoomFog, CharacterAwakenTimer, TakeDownThePortalTextTitle,
        // ChatWindow, plus the 12.2 recorded OPT-OUTs SelectPanelPlayer / AwakeningRecapMessages /
        // AnonymousRevealedMessagesComponent (no injection context). ChatManager / LobbyPlayerInfoHolder → Epic 10;
        // W* winning-condition POCOs + TargetUtils = static/POCO façade (no injection context);
        // DevIdentityController = debug F-keys. The field STAYS public for those callers.
        // recorded §4 survivor (12.3 strategy B): kept as a verify-don't-force exception, NOT deleted — read
        // only by context-less static machinery (W*/TargetUtils) + ChatManager's NFR5
        // GetSafeRpcTarget + the network fixtures. Enforced by StaticSingletonCensusGuardTests.
        public static CharacterManager instance;

        // Per-NetworkManager registry: lets a second in-process client's replica
        // coexist (resolved via For(NetworkManager)) instead of clobbering or
        // destroying the primary's static instance. The primary manager keeps the
        // historical `instance` façade so the Awake->spawn window is unchanged.
        private static readonly Dictionary<NetworkManager, CharacterManager> s_byNetworkManager = new();

        /// <summary>
        /// Resolves the CharacterManager owned by the given NetworkManager. For the
        /// primary (Singleton) manager this falls back to the Awake-claimed instance
        /// so the pre-spawn window behaves exactly as the historical static access.
        ///
        /// Story 9.3 (Epic 9 / D3): this is the per-NetworkManager BACKBONE the CompositionRoot
        /// delegates to (CompositionRoot.For(nm).Character* -> here). It stays public because the
        /// production resolution path runs through it, but the only DIRECT callers of bare
        /// CharacterManager.For are now the root and the test fixtures (AC1) — gameplay code resolves
        /// via CompositionRoot. Absorbing this into the root is deferred (per the 6.3 design, recorded).
        /// </summary>
        public static CharacterManager For(NetworkManager _networkManager)
        {
            if (_networkManager != null && s_byNetworkManager.TryGetValue(_networkManager, out var _manager) && _manager != null)
            {
                return _manager;
            }
            return _networkManager == NetworkManager.Singleton ? instance : null;
        }

#if UNITY_EDITOR
        // Play-restart backstop ONLY. Domain reload is disabled in this project, so
        // statics survive across Play Mode sessions; this fires once at Play entry
        // (SubsystemRegistration) to drop any manager/registry left over from a prior
        // session. It does NOT run on scene loads and is NOT a subscription cleanup:
        // the networkedCharacters.OnValueChanged unsubscribe and the registry/instance
        // teardown for normal scene exit (incl. menu -> scene -> menu round-trips) live
        // in OnNetworkDespawn and OnDestroy, which fire because CharacterManager is
        // scene-placed in GameScene (Shutdown despawns, single-mode LoadScene destroys).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled()
        {
            s_byNetworkManager.Clear();
            instance = null;
        }
#endif

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

        // Tripwire memory: a duplicated replica entry persists in networkedCharacters
        // for the whole session (clients cannot repair a server-write NetworkList) and
        // the rebuild reruns on every list change, so without this set the [CHARLIST]
        // error would flood the log on every dirty read.
        private readonly HashSet<ulong> _reportedDuplicateObjectIds = new();

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
            // NET-04: ids resolved through THIS manager's own NetworkManager (a bare NetworkBehaviourReference.TryGet
            // resolved against NetworkManager.Singleton, i.e. the host, in 2-NM setups).
            var _spawned = NetworkManager != null && NetworkManager.SpawnManager != null
                ? NetworkManager.SpawnManager.SpawnedObjects
                : null;
            foreach (ulong _objectId in ReplicatedCharacterObjectIds)
            {
                if (_spawned != null
                    && _spawned.TryGetValue(_objectId, out NetworkObject _networkObject)
                    && _networkObject != null
                    && _networkObject.TryGetComponent(out Character _character))
                {
                    // Self-healing projection: NGO can deliver the same list entry
                    // twice to a joining client (initial-sync + pending-delta race —
                    // see investigations/technomancer-duplicate-card-investigation.md).
                    // A legitimate game can never hold the same Character instance
                    // twice (AddNewCharacter dedups by clientId), so dropping by
                    // reference is safe. The loud log is a permanent tripwire that
                    // proves the replica divergence in Player.log when it recurs.
                    if (_charactersCache.Contains(_character))
                    {
                        if (_reportedDuplicateObjectIds.Add(_character.NetworkObjectId))
                        {
                            Debug.LogError($"[CHARLIST] Duplicate networkedCharacters entry dropped: ownerClientId={_character.ownerClientId.Value} networkObjectId={_character.NetworkObjectId} listCount={ReplicatedCharacterObjectIds.Count}");
                        }
                        continue;
                    }
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

        private void OnNetworkedCharactersChanged(NetworkObjectIdList _previous, NetworkObjectIdList _current)
        {
            // Authoritative source changed: force a rebuild on next access.
            _cacheDirty = true;
        }

        // NET-04 (epic-network-sync-hardening): the character list is ONE full-value snapshot of NetworkObject ids in
        // a NetworkVariable, never a NetworkList (index RemoveAt on a #3280-diverged replica removed the WRONG
        // character client-side, and the list order is card order). Server writes assign a NEW list.
        private readonly NetworkVariable<NetworkObjectIdList> networkedCharacters = new(new NetworkObjectIdList());

        /// <summary>The replicated character NetworkObject ids, in authoritative order (read-only, every peer).</summary>
        public IReadOnlyList<ulong> ReplicatedCharacterObjectIds =>
            networkedCharacters.Value != null ? networkedCharacters.Value.Ids : Array.Empty<ulong>();
        
        public event Action<List<Character>> onCharactersListUpdated;
        public event Action onLocalIdentityChanged;

        private ulong? _debugPossessedId = null;
        
        private void Awake()
        {
            // Design B (NGO probe: NetworkManagerOwner is assigned AFTER
            // Object.Instantiate returns - NetworkSpawnManager.cs:881 vs
            // SpawnNetworkObjectLocally:1055 - so it is not visible here). Awake
            // cannot tell a foreign-NM replica from a true duplicate, so it only
            // claims the façade if free; same-NM duplicate destruction and
            // foreign-replica reconciliation happen in OnNetworkSpawn where
            // NetworkManager is authoritative. Production has exactly one
            // scene-placed CharacterManager, so this is behaviour-identical there.
            if (instance == null)
            {
                instance = this;
            }
        }
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            var _networkManager = NetworkManager;

            // Same-NM duplicate (today's semantics, one frame later than the
            // historical Awake destroy): a live manager is already registered for
            // this NetworkManager -> this is an extra instance, destroy it.
            if (s_byNetworkManager.TryGetValue(_networkManager, out var _existing) && _existing != null && _existing != this)
            {
                Destroy(gameObject);
                return;
            }

            // Façade reconciliation: the primary (Singleton) manager owns `instance`,
            // a foreign-NM replica must not. Because Awake claims-if-free, whichever
            // CharacterManager awoke first holds the claim - release/transfer it here
            // now that NetworkManager is authoritative.
            if (_networkManager != NetworkManager.Singleton)
            {
                if (instance == this)
                {
                    instance = null;
                }
            }
            else if (instance == null)
            {
                instance = this;
            }

            // Registry claim (the inherited NetworkManager property is valid here).
            s_byNetworkManager[_networkManager] = this;

            // Subscribe to the authoritative list so the cache is invalidated
            // whenever it changes.
            networkedCharacters.OnValueChanged += OnNetworkedCharactersChanged;

            // The NetworkList is delivered already populated to late joiners
            // without raising OnListChanged for the initial state, so force a
            // rebuild here (kept dirty until every reference resolves).
            _cacheDirty = true;
        }

        public override void OnNetworkDespawn()
        {
            networkedCharacters.OnValueChanged -= OnNetworkedCharactersChanged;

            UnregisterFromRegistry();

            if (instance == this)
            {
                instance = null;
            }

            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            // Safety net: a same-NM duplicate is destroyed in OnNetworkSpawn and may
            // never despawn cleanly; also covers teardown ordering where
            // OnNetworkDespawn was not invoked. Unsubscribing twice is harmless.
            networkedCharacters.OnValueChanged -= OnNetworkedCharactersChanged;

            UnregisterFromRegistry();

            if (instance == this)
            {
                instance = null;
            }
        }

        // Removes this manager from the per-NetworkManager registry by value, so
        // teardown ordering (NGO: NetworkManager.Singleton may be null in OnDestroy
        // during shutdown) can never strand a stale entry or throw on a stale key.
        // The registry holds at most a handful of entries, so the scan is trivial.
        private void UnregisterFromRegistry()
        {
            NetworkManager _key = null;
            foreach (var _pair in s_byNetworkManager)
            {
                if (_pair.Value == this)
                {
                    _key = _pair.Key;
                    break;
                }
            }
            if (_key != null)
            {
                s_byNetworkManager.Remove(_key);
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
            if (!IsSpawned || !IsServer)
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

            // Authoritative source (NET-04: assign a NEW id list — full-value replication).
            networkedCharacters.Value = networkedCharacters.Value.WithAdded(_newCharacterObject.NetworkObjectId);

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
                // Remove from the authoritative source BEFORE despawning (NET-04: by id, never by index — a
                // replica can no longer lose the wrong character).
                networkedCharacters.Value = networkedCharacters.Value.WithRemoved(_characterToRemove.NetworkObjectId);

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
        
        public void GivePowerToCharacter(ulong _characterId, Power _power, System.Action<Power> _onReady = null)
        {
            Assert.IsTrue(NetworkManager.IsServer, "GivePowerToCharacter should only be called on the server");
            Character _character = GetCharacter(_characterId);
            Assert.IsNotNull(_character, $"Character with id {_characterId} not found when trying to give power {_power.powerName}");

            // Clone the BASE PREFAB, not the (possibly runtime-mutated) instance passed in, so a copy always starts
            // fresh. Initial attribution + Legacy pass prefabs already (basePrefab null → _source == _power, no change);
            // the copiers pass live instances whose basePrefab points to the prefab → they now clone fresh.
            Power _source = _power != null && _power.basePrefab != null ? _power.basePrefab : _power;
            Power _newPower = Instantiate(_source, null);
            _newPower.basePrefab = _source;
            NetworkObject _powerNetworkObject = _newPower.GetComponent<NetworkObject>();
            _powerNetworkObject.GetComponent<Power>().idHolderServer = _characterId;
            _powerNetworkObject.Spawn(true);
            StartCoroutine(
                WaitForParentToSpawnAndSet(_powerNetworkObject, _character.GetComponent<NetworkObject>(),
                    (_result) =>
                    {
                        OnPowerReparentComplete(_newPower, _result);
                        // Fired AFTER the copy is reparented + registered in the new owner's role.powers, so a
                        // caller (e.g. Marque d'Hurluberluges) can configure the freshly-given instance.
                        if (_result)
                        {
                            _onReady?.Invoke(_newPower);
                        }
                    })
                );
        }
        
        public void RemovePowerFromCharacter(ulong _characterId, Power _power)
        {
            Assert.IsTrue(NetworkManager.IsServer, "RemovePowerFromCharacter should only be called on the server");
            Character _character = GetCharacter(_characterId);
            // Owner may have vanished the same frame the copy is spent (disconnect chain): quiet no-op, not an assert.
            if (_character == null || _power == null)
            {
                return;
            }

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

            PowerManager.instance?.OnPowerReparentedServer(_power);
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