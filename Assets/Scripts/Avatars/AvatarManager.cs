using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Avatars
{
    /// <summary>
    /// Story 13.1 (Epic 13 — Player Embodiment &amp; Physical Presence). Scene-placed, server-authoritative
    /// avatar spawner + per-NetworkManager registry + seat/spawn registry. Mirrors the proven
    /// CharacterManager lifecycle (CharacterManager.cs:38–287, 400–462) and the LobbyState
    /// connect/disconnect driver (LobbyState.cs:26–75).
    ///
    /// BORN CLEAN (the refactor's end-state goal): NO <c>static instance</c> façade — resolution is
    /// <see cref="For"/>(nm)-only (mirrors CompositionRoot's For-based design), so the endgame
    /// StaticSingletonCensusGuard needs no new whitelist entry. The per-NetworkManager registry is what
    /// lets the MultiClientGameFixture's second in-process NM resolve its own avatar manager.
    ///
    /// SCOPE: spawn + persist + despawn + seat registry + appearance hook ONLY. No movement (13.2),
    /// camera (13.3), embodied vote (13.4), or voice (13.5/13.6). Simulated bots (clientId &gt;= 100)
    /// get NO avatar (DO2 / AC #6); GetSafeRpcTarget / IsLocalOrSimulated / the bot-debug flow are
    /// untouched (NFR4). Avatars persist for the whole match (AC #2): spawned once on connect, never
    /// re-spawned per phase; destroyWithScene:true cleans them up at match-end scene unload.
    /// </summary>
    public class AvatarManager : NetworkBehaviour
    {
        // Per-NetworkManager registry (mirror CharacterManager.s_byNetworkManager). No `instance` façade:
        // a fresh manager with no legacy callers is born without one — the census-guard end-state.
        private static readonly Dictionary<NetworkManager, AvatarManager> s_byNetworkManager = new();

        /// <summary>
        /// Resolves the AvatarManager owned by the given NetworkManager. For(nm)-only backbone (mirror
        /// CharacterManager.For) — this is what lets a second in-process NM resolve its own manager. It is
        /// NOT a forbidden locator: guard #1 forbids GameManager.For/CharacterManager.For, not AvatarManager.For.
        /// </summary>
        public static AvatarManager For(NetworkManager _networkManager)
        {
            if (_networkManager != null && s_byNetworkManager.TryGetValue(_networkManager, out var _manager) && _manager != null)
            {
                return _manager;
            }
            return null;
        }

#if UNITY_EDITOR
        // Domain reload is disabled — statics survive Play sessions (project-context.md §Domain reload).
        // Drop any registry entry left from a prior session at Play entry (mirror CharacterManager.cs:75).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled()
        {
            s_byNetworkManager.Clear();
        }
#endif

        [SerializeField] private NetworkObject _avatarPrefab;
        [SerializeField] private Transform _avatarsParent;

        [Header("Seat ring (procedural — replaces the fixed seat list)")]
        [Tooltip("The single table-ring CENTER. Seats are computed as an evenly-spaced circle around it; " +
                 "its forward (+Z) is the FRONT direction the local player is rotated onto. Wire in GameScene.")]
        [SerializeField] private Transform _ringCenter;
        [Tooltip("Ring radius (metres) from the center to each seat. Used for the LOCAL player (the POV) so " +
                 "the camera stays close for vision.")]
        [SerializeField] private float _ringRadius = 3f;
        [Tooltip("Ring radius (metres) for the OTHER players' avatars only — lets remote bodies sit further " +
                 "out so they don't clip through the table while the local POV stays at _ringRadius. The head " +
                 "look stays relative to seat facing, so 'who looks at whom' is preserved in direction (the " +
                 "remote head origin just shifts outward — a small parallax on close neighbours). <= 0 means " +
                 "'same as _ringRadius' (uniform ring — original behaviour, the safe default).")]
        [SerializeField] private float _remoteRingRadius = 0f;
        [Tooltip("Angle (deg) of the FRONT spot from the center forward — where the LOCAL player always sits.")]
        [SerializeField] private float _frontAngleDeg = 0f;
        [Tooltip("Lobby spawn points where avatars appear, in a STABLE order.")]
        [SerializeField] private List<Transform> _spawnPoints = new();

        // One-shot warn guard so a missing ring center logs once, not every frame the presenter polls.
        private bool _warnedNoRingCenter;

        // Authoritative, replicated set of spawned avatars (mirror CharacterManager.networkedCharacters):
        // late joiners receive it pre-populated WITHOUT OnListChanged, so OnNetworkSpawn force-rebuilds.
        // Field initializer (NOT Awake): NGO collects NetworkList fields via reflection at init time.
        // NET-04 (epic-network-sync-hardening): full-value snapshot of avatar NetworkObject ids (never a NetworkList:
        // index RemoveAt on a #3280-diverged replica removed the wrong avatar, and list order is the seat index).
        private readonly NetworkVariable<Network.NetworkObjectIdList> _avatars = new(new Network.NetworkObjectIdList());

        // Backing cache rebuilt only when _avatars changes (OnValueChanged) or on spawn, so reads
        // allocate nothing in steady state (mirror CharacterManager._charactersCache).
        private readonly List<PlayerAvatar> _avatarsCache = new();
        private bool _cacheDirty = true;

        // Lane C: the read slice, resolved ONCE in OnNetworkSpawn (Character.cs:49 precedent). Completes
        // the clientId <-> Character <-> seat mapping (AC #3). Null-tolerant — degrades gracefully if no
        // CharacterManager is registered for this NM (e.g. an avatar-only test harness).
        private ICharacterQuery _characterQuery;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            var _networkManager = NetworkManager;

            // Same-NM duplicate guard (mirror CharacterManager.cs:203): exactly one manager per NM.
            // Production has one scene-placed AvatarManager, so this never triggers there.
            if (s_byNetworkManager.TryGetValue(_networkManager, out var _existing) && _existing != null && _existing != this)
            {
                Destroy(gameObject);
                return;
            }
            s_byNetworkManager[_networkManager] = this;

            // Lane C resolution (sanctioned ONLY inside OnNetworkSpawn). CompositionRoot.For(nm) returns a
            // lightweight value resolver and needs no scene root — CharacterQuery is null when no
            // CharacterManager is registered for this NM, which the seat mapping tolerates.
            _characterQuery = CompositionRoot.For(_networkManager).CharacterQuery;

            // Authoritative list -> cache invalidation (mirror CharacterManager.cs:230). Late joiners get
            // the list pre-populated without OnListChanged, so force a rebuild here.
            _avatars.OnValueChanged += OnAvatarsChanged;
            _cacheDirty = true;

            // Server-only spawn driver (mirror LobbyState.cs:57–69): spawn for already-connected real
            // clients (incl. the host itself), then keep spawning/despawning on connect/disconnect.
            if (IsServer)
            {
                Assert.IsNotNull(_avatarPrefab,
                    "AvatarManager._avatarPrefab is not wired — wire it in GameScene (the avatar prefab to spawn).");

                foreach (var _client in _networkManager.ConnectedClients)
                {
                    SpawnAvatar(_client.Key);
                }
                _networkManager.OnClientConnectedCallback += OnClientConnected;
                _networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            if (_avatars != null)
            {
                _avatars.OnValueChanged -= OnAvatarsChanged;
            }
            UnregisterFromRegistry();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            // Safety net: NetworkManager may be null during shutdown teardown (project-context.md
            // §Gotchas) — never key off it. Unsubscribing twice is harmless.
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            if (_avatars != null)
            {
                _avatars.OnValueChanged -= OnAvatarsChanged;
            }
            UnregisterFromRegistry();
        }

        // Removes this manager from the per-NetworkManager registry by value, so teardown ordering can
        // never strand a stale entry or throw on a stale key (mirror CharacterManager.UnregisterFromRegistry).
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

        private void OnClientConnected(ulong _clientId) => SpawnAvatar(_clientId);
        private void OnClientDisconnected(ulong _clientId) => DespawnAvatar(_clientId);

        /// <summary>
        /// Server-side per-client avatar spawn (mirror CharacterManager.AddNewCharacter, CharacterManager.cs:400).
        /// Bots (clientId &gt;= 100) get NO avatar (AC #6 / DO2). Idempotent: a second call for an
        /// already-avatared client is a no-op. Returns the spawned avatar (null when skipped).
        /// </summary>
        public PlayerAvatar SpawnAvatar(ulong _clientId)
        {
            Assert.IsTrue(IsServer, "SpawnAvatar must run on the server.");

            // AC #6 (DO2): simulated bots get no body. The bot-debug flow (GetSafeRpcTarget /
            // IsLocalOrSimulated) is untouched — bots simply have no avatar.
            if (_clientId >= 100)
            {
                return null;
            }

            // Guard double-spawn for an existing client (mirror CharacterManager.cs:402).
            if (GetAvatar(_clientId) != null)
            {
                return null;
            }

            // Story 13.2: spawn OWNED by the target client so its owner-authoritative NetworkTransform
            // drives the local movement (NFR3 — cosmetic position only; game state stays server-auth).
            // Position is passed at spawn so it rides the spawn payload and lands correctly on every
            // replica regardless of NetworkTransform authority (a server-set transform on a remote-owned
            // object would NOT replicate under owner authority). Bots never reach here (clientId < 100 above).
            Transform _spawn = GetSpawnPoint(_clientId);
            Vector3 _spawnPosition = _spawn != null ? _spawn.position : Vector3.zero;
            Quaternion _spawnRotation = _spawn != null ? _spawn.rotation : Quaternion.identity;

            NetworkObject _avatarObject = NetworkManager.SpawnManager.InstantiateAndSpawn(
                _avatarPrefab, ownerClientId: _clientId, destroyWithScene: true,
                position: _spawnPosition, rotation: _spawnRotation);

            // Reparent under the scene parent if wired; defer until it is spawned (mirror CharacterManager.cs:409).
            if (_avatarsParent != null)
            {
                var _parentNo = _avatarsParent.GetComponent<NetworkObject>();
                if (_parentNo != null && !_parentNo.IsSpawned)
                {
                    StartCoroutine(WaitForParentToSpawnAndSet(_avatarObject, _parentNo));
                }
                else if (_parentNo != null)
                {
                    _avatarObject.TrySetParent(_avatarsParent, false);
                }
            }

            PlayerAvatar _avatar = _avatarObject.GetComponent<PlayerAvatar>();
            _avatar.ownerClientId.Value = _clientId;

            // Authoritative source (NET-04: assign a NEW id list — full-value replication, late-joiner safe).
            _avatars.Value = _avatars.Value.WithAdded(_avatarObject.NetworkObjectId);
            _cacheDirty = true;
            return _avatar;
        }

        /// <summary>
        /// Server-side despawn for a client (mirror CharacterManager.RemoveCharacter, CharacterManager.cs:434).
        /// Removes from the authoritative list BEFORE despawn (its reference stops resolving once despawned),
        /// then Despawn(destroy:true) — never a client-side Destroy (AC #1).
        /// </summary>
        public void DespawnAvatar(ulong _clientId)
        {
            Assert.IsTrue(IsServer, "DespawnAvatar must run on the server.");

            PlayerAvatar _avatar = GetAvatar(_clientId);
            if (_avatar == null)
            {
                return;
            }

            // NET-04: by id, never by index.
            _avatars.Value = _avatars.Value.WithRemoved(_avatar.NetworkObjectId);

            var _no = _avatar.GetComponent<NetworkObject>();
            if (_no != null && _no.IsSpawned)
            {
                _no.Despawn(true);
            }
            _cacheDirty = true;
        }

        // --- seat / spawn registry (AC #3) ---

        /// <summary>
        /// The avatar's stable index = its position in the replicated avatar list (identical on every
        /// client, since the list order replicates). A not-yet-listed client (mid-spawn) gets the next
        /// free slot. NOTE: a disconnect re-indexes later avatars — acceptable for the 13.1 foundation;
        /// the real front-seat seating is 13.4.
        /// </summary>
        private int SeatIndexForClient(ulong _clientId)
        {
            var _cache = ResolvedAvatars();
            for (int _i = 0; _i < _cache.Count; _i++)
            {
                if (_cache[_i] != null && _cache[_i].ownerClientId.Value == _clientId)
                {
                    return _i;
                }
            }
            return _cache.Count;
        }

        /// <summary>
        /// The seat pose for <paramref name="_clientId"/>, computed in the LOCAL client's rotated frame:
        /// the local player's own avatar lands at the fixed FRONT spot (relative offset 0), everyone else
        /// is spread equidistant around the ring by their relative position in the replicated avatar list.
        /// Positions are therefore client-local (NOT networked) but the relative arrangement is identical
        /// everywhere (gaze-preserving). Null-tolerant: returns the front pose / identity if no ring center
        /// is wired (warns once) — never crashes.
        /// </summary>
        public SeatPose GetSeatPose(ulong _clientId)
        {
            if (_ringCenter == null)
            {
                if (!_warnedNoRingCenter)
                {
                    Debug.LogWarning("AvatarManager._ringCenter is not wired — seats collapse to the origin. Wire it in GameScene.");
                    _warnedNoRingCenter = true;
                }
                return new SeatPose(Vector3.zero, Quaternion.identity);
            }

            ulong _localId = NetworkManager != null ? NetworkManager.LocalClientId : _clientId;
            // The LOCAL avatar (the POV) always uses _ringRadius so the embodied camera stays close; OTHER
            // avatars use _remoteRingRadius when it is set (> 0) so their bodies sit further out and don't clip
            // the table. The gaze yaw is applied as a head-local rotation relative to seat facing (unchanged by
            // radius), so the look DIRECTION is preserved — only a pushed-out remote head's origin shifts.
            bool _isLocal = _clientId == _localId;
            float _radius = _isLocal || _remoteRingRadius <= 0f ? _ringRadius : _remoteRingRadius;
            return SeatRingGeometry.Compute(
                AvatarCount,
                SeatIndexForClient(_localId),
                SeatIndexForClient(_clientId),
                _ringCenter.position,
                _ringCenter.forward,
                _radius,
                _frontAngleDeg);
        }

        /// <summary>The lobby spawn point for a client (null if none wired). AC #3.</summary>
        public Transform GetSpawnPoint(ulong _clientId)
        {
            if (_spawnPoints == null || _spawnPoints.Count == 0)
            {
                return null;
            }
            return _spawnPoints[SeatIndexForClient(_clientId) % _spawnPoints.Count];
        }

        /// <summary>The Character mapped to a client (null when unresolved) — completes the
        /// clientId &lt;-&gt; Character &lt;-&gt; seat mapping (AC #3) via the lane-C read slice.</summary>
        public Character GetCharacterForClient(ulong _clientId) => _characterQuery?.GetCharacter(_clientId, false);

        // --- avatar lookup (cache mirrors the replicated list) ---

        public PlayerAvatar GetAvatar(ulong _clientId) =>
            ResolvedAvatars().FirstOrDefault(_a => _a != null && _a.ownerClientId.Value == _clientId);

        public IReadOnlyList<PlayerAvatar> GetAvatars() => ResolvedAvatars();

        public int AvatarCount => ResolvedAvatars().Count;

        private List<PlayerAvatar> ResolvedAvatars()
        {
            if (_cacheDirty)
            {
                RebuildCache();
            }
            return _avatarsCache;
        }

        private void RebuildCache()
        {
            _avatarsCache.Clear();
            bool _allResolved = true;
            // NET-04: resolve ids through THIS manager's own NetworkManager.
            var _spawned = NetworkManager != null && NetworkManager.SpawnManager != null
                ? NetworkManager.SpawnManager.SpawnedObjects
                : null;
            foreach (ulong _objectId in _avatars.Value != null ? _avatars.Value.Ids : System.Array.Empty<ulong>())
            {
                if (_spawned != null
                    && _spawned.TryGetValue(_objectId, out NetworkObject _networkObject)
                    && _networkObject != null
                    && _networkObject.TryGetComponent(out PlayerAvatar _avatar))
                {
                    if (!_avatarsCache.Contains(_avatar))
                    {
                        _avatarsCache.Add(_avatar);
                    }
                }
                else
                {
                    // Spawn still in flight: skip (no crash) and keep the cache dirty so a later read
                    // picks it up (mirror CharacterManager.RebuildCharactersCache).
                    _allResolved = false;
                }
            }
            _cacheDirty = !_allResolved;
        }

        private void OnAvatarsChanged(Network.NetworkObjectIdList _previous, Network.NetworkObjectIdList _current) => _cacheDirty = true;

        private IEnumerator WaitForParentToSpawnAndSet(NetworkObject _child, NetworkObject _parent)
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
            }
        }
    }
}
