using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using CorruptionDuPortail.Domain.PlayerIcons;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// Server-authoritative registry of PRIVATE power icons drawn on the CharactersBar thumbnails.
    ///
    /// A marker is the triple (iconId, marked player, viewer) and has EXACTLY ONE viewer: "my power
    /// marked this person, in MY view". The full table lives on the server only; each client receives
    /// nothing but its OWN slice, pushed by a targeted RPC. That privacy is the whole point of the
    /// subsystem — a NetworkList or a SendTo.Everyone broadcast would hand every player the complete map
    /// of who is marked by what, and the leak would be INVISIBLE in a host-only playtest (the host
    /// legitimately sees everything). See PlayerIconPrivacyTests for the 2-NetworkManager proof.
    ///
    /// BORN CLEAN (mirror <see cref="Avatars.AvatarManager"/>): NO <c>static instance</c> — resolution is
    /// <see cref="For"/>(nm)-only, so the StaticSingletonCensusGuard needs no new whitelist entry and the
    /// second in-process NetworkManager of the multi-client fixture resolves its own manager.
    /// </summary>
    public class PlayerIconManager : NetworkBehaviour
    {
        /// <summary>
        /// One private marker. <c>IconId</c> is the declaring Power's NetworkObjectId — the receiving
        /// client resolves it back to that Power's <c>BarIcon</c> sprite, so no icon registry asset is
        /// needed and a power with no sprite simply draws nothing.
        /// </summary>
        public readonly struct Marker : IEquatable<Marker>
        {
            public readonly ulong IconId;
            public readonly ulong MarkedClientId;
            public readonly ulong ViewerClientId;
            public readonly PlayerIconLifetime Lifetime;

            public Marker(ulong _iconId, ulong _markedClientId, ulong _viewerClientId, PlayerIconLifetime _lifetime)
            {
                IconId = _iconId;
                MarkedClientId = _markedClientId;
                ViewerClientId = _viewerClientId;
                Lifetime = _lifetime;
            }

            /// <summary>Identity is (icon, marked, viewer) — the LIFETIME is deliberately excluded so a
            /// re-add with a different lifetime is still recognised as the same marker (dedup rule).</summary>
            public bool Equals(Marker _other) =>
                IconId == _other.IconId && MarkedClientId == _other.MarkedClientId && ViewerClientId == _other.ViewerClientId;

            public override bool Equals(object _obj) => _obj is Marker _o && Equals(_o);

            public override int GetHashCode()
            {
                unchecked
                {
                    int _hash = IconId.GetHashCode();
                    _hash = (_hash * 397) ^ MarkedClientId.GetHashCode();
                    _hash = (_hash * 397) ^ ViewerClientId.GetHashCode();
                    return _hash;
                }
            }
        }

        /// <summary>
        /// Wire payload of one marker in a viewer's slice. The viewer TRAVELS with the entry (iteration 2):
        /// it is NOT implicitly the recipient, because a simulated bot's slice is delivered to the HOST by
        /// GetSafeRpcTarget. Without the viewer on the payload the receiving peer cannot tell whose slice it
        /// just got, and the bot's slice silently overwrote the host's own.
        /// </summary>
        public struct IconEntry : INetworkSerializable, IEquatable<IconEntry>
        {
            public ulong IconId;
            public ulong MarkedClientId;
            public ulong ViewerClientId;

            public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
            {
                _serializer.SerializeValue(ref IconId);
                _serializer.SerializeValue(ref MarkedClientId);
                _serializer.SerializeValue(ref ViewerClientId);
            }

            public bool Equals(IconEntry _other) =>
                IconId == _other.IconId && MarkedClientId == _other.MarkedClientId && ViewerClientId == _other.ViewerClientId;
        }

        // Slots are LOGICAL ints that the executors cast to ulong (1:1 with clientIds in this codebase).
        // A NEGATIVE slot casts to a huge ulong, and ulong.MaxValue is the codebase's "no observer"
        // sentinel (GameInfoRevealer.GetCharacterInfo) — neither is a real client, so both are refused
        // rather than silently creating a bucket nobody can ever read.
        private const ulong InvalidClientIdFloor = 0xFFFFFFFF00000000UL;

        private static bool IsInvalidClientId(ulong _clientId) => _clientId >= InvalidClientIdFloor;

        // How many consecutive failed re-resolutions before the console is told. A dependency that is
        // merely late (spawn-order race) resolves within a frame or two; one that is durably absent must
        // never fail SILENTLY — that is exactly the invisible breakage iteration 2 exists to kill.
        private const int MissesBeforeLoggingFailure = 3;

        // Per-NetworkManager registry (mirror AvatarManager.s_byNetworkManager). No `instance` façade.
        private static readonly Dictionary<NetworkManager, PlayerIconManager> s_byNetworkManager = new();

        /// <summary>Resolves the PlayerIconManager owned by the given NetworkManager (null when none).</summary>
        public static PlayerIconManager For(NetworkManager _networkManager)
        {
            if (_networkManager != null && s_byNetworkManager.TryGetValue(_networkManager, out var _manager) && _manager != null)
            {
                return _manager;
            }
            return null;
        }

#if UNITY_EDITOR
        // Domain reload is disabled — statics survive Play sessions (project-context.md §Domain reload).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled()
        {
            s_byNetworkManager.Clear();
        }
#endif

        // SERVER-ONLY authoritative table, keyed by viewer. Never replicated wholesale (see class doc).
        private readonly Dictionary<ulong, List<Marker>> _byViewer = new();

        // CLIENT-side slices, keyed BY VIEWER. A plain client only ever holds one key (its own id). The
        // HOST holds its own slice PLUS one per simulated bot whose RPC GetSafeRpcTarget redirected here —
        // the same shape, and for the same reason, as GameInfoRevealer.simulationsKnowledge. One flat list
        // would let a bot's slice wipe the host's own on every push.
        private readonly Dictionary<ulong, List<IconEntry>> _localByViewer = new();

        /// <summary>Raised on this peer whenever any slice it holds changes — the icon-stack views rebuild on it.</summary>
        public event Action onLocalIconsChanged;

        private CharacterManager _characterManager;
        private int _characterManagerMisses;
        private bool _loggedCharacterManagerFailure;

        private readonly List<AwakeningState> _subscribedAwakeningStates = new();
        private Action _onAwakeningStartHandler;
        private bool _awakeningSubscriptionResolved;
        private int _gameManagerMisses;
        private bool _loggedGameManagerFailure;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            var _networkManager = NetworkManager;

            // Same-NM duplicate guard (mirror AvatarManager): exactly one manager per NetworkManager.
            if (s_byNetworkManager.TryGetValue(_networkManager, out var _existing) && _existing != null && _existing != this)
            {
                Destroy(gameObject);
                return;
            }
            s_byNetworkManager[_networkManager] = this;

            // FIRST attempt only. NGO guarantees no cross-object spawn order, so a null here is normal and
            // NOT terminal — every use site re-resolves through ResolveCharacterManager (iteration 2 §2).
            _characterManager = CompositionRoot.For(_networkManager).CharacterManager;

            if (IsServer)
            {
                // Late joiners get their (possibly empty) slice on connect; already-connected clients —
                // including the host itself — are swept now (mirror LobbyPlayerInfoHolder.OnNetworkSpawn).
                _networkManager.OnClientConnectedCallback += OnClientConnected;
                _networkManager.OnClientDisconnectCallback += OnClientDisconnected;
                foreach (var _client in _networkManager.ConnectedClients)
                {
                    PushSliceTo(_client.Key);
                }

                EnsureAwakeningSubscription();
            }
        }

        // ---- lazy dependency resolution (iteration 2 §2) ------------------------------------------

        /// <summary>
        /// Re-resolves the CharacterManager AT USE. Cross-object spawn order is not guaranteed in NGO, so
        /// resolving once in OnNetworkSpawn and giving up is a silent death: every remote viewer would stop
        /// receiving icons for the whole session, and a host-only playtest would never show it. A durable
        /// failure is reported to the console instead of returning quietly.
        /// </summary>
        private CharacterManager ResolveCharacterManager()
        {
            if (_characterManager != null)
            {
                return _characterManager;
            }

            NetworkManager _networkManager = NetworkManager;
            if (_networkManager != null)
            {
                _characterManager = CompositionRoot.For(_networkManager).CharacterManager;
            }

            if (_characterManager == null)
            {
                _characterManagerMisses++;
                if (_characterManagerMisses >= MissesBeforeLoggingFailure && !_loggedCharacterManagerFailure)
                {
                    _loggedCharacterManagerFailure = true;
                    Debug.LogError(
                        $"[PLAYERICONS] CharacterManager still unresolved after {_characterManagerMisses} attempts — " +
                        "private icon slices cannot be delivered to remote viewers (GetSafeRpcTarget is unreachable).",
                        this);
                }
            }
            else
            {
                _characterManagerMisses = 0;
            }
            return _characterManager;
        }

        /// <summary>
        /// Subscribes to the awakening purge, retrying at use until the GameManager exists. Without the retry
        /// a spawn-order race leaves transient markers never purged for the whole session — silently.
        /// A GameManager that resolves but declares NO AwakeningState is a legitimate harness, not a failure:
        /// resolution counts as done and nothing is logged.
        /// </summary>
        private void EnsureAwakeningSubscription()
        {
            if (!IsServer || _awakeningSubscriptionResolved)
            {
                return;
            }

            NetworkManager _networkManager = NetworkManager;
            if (_networkManager == null)
            {
                return;
            }

            GameManager _gameManager = CompositionRoot.For(_networkManager).GameManager;
            if (_gameManager == null)
            {
                _gameManagerMisses++;
                if (_gameManagerMisses >= MissesBeforeLoggingFailure && !_loggedGameManagerFailure)
                {
                    _loggedGameManagerFailure = true;
                    Debug.LogError(
                        $"[PLAYERICONS] GameManager still unresolved after {_gameManagerMisses} attempts — " +
                        "ClearAtAwakeningStart markers will never be purged.",
                        this);
                }
                return;
            }

            _awakeningSubscriptionResolved = true;
            _onAwakeningStartHandler = ClearTransientMarkers;
            foreach (var _state in _gameManager.GetGameStates(typeof(AwakeningState)))
            {
                if (_state is AwakeningState _awakening)
                {
                    _awakening.onStateStartServer += _onAwakeningStartHandler;
                    _subscribedAwakeningStates.Add(_awakening);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            Teardown();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            Teardown();
        }

        // NetworkManager may already be null during shutdown teardown — never key off it. Idempotent.
        private void Teardown()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            if (_onAwakeningStartHandler != null)
            {
                foreach (var _state in _subscribedAwakeningStates)
                {
                    if (_state != null)
                    {
                        _state.onStateStartServer -= _onAwakeningStartHandler;
                    }
                }
                _onAwakeningStartHandler = null;
            }
            _subscribedAwakeningStates.Clear();
            _awakeningSubscriptionResolved = false;
            // Both tables die with the manager. Domain reload is disabled, so a surviving server table
            // would carry a previous session's markers into the next one, and a surviving local slice
            // would let a stale icon outlive its own manager.
            _byViewer.Clear();
            _localByViewer.Clear();
            UnregisterFromRegistry();
        }

        // Removes this manager from the registry BY VALUE so teardown ordering can never strand a stale
        // entry or throw on a stale key (mirror AvatarManager.UnregisterFromRegistry).
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

        // ---- server-side mutation ---------------------------------------------------------------

        /// <summary>
        /// Server-side: register a private marker and push the viewer's refreshed slice. Exact duplicates
        /// (same icon, same marked player, same viewer) are silently deduplicated — the second call is a
        /// no-op and pushes nothing.
        /// </summary>
        public void AddIcon(ulong _iconId, ulong _markedClientId, ulong _viewerClientId, PlayerIconLifetime _lifetime)
        {
            if (!IsServer)
            {
                return;
            }
            if (IsInvalidClientId(_markedClientId) || IsInvalidClientId(_viewerClientId))
            {
                Debug.LogWarning(
                    $"[PLAYERICONS] Refused a marker with an out-of-range id (marked={_markedClientId}, viewer={_viewerClientId}) " +
                    "— a negative slot or the ulong.MaxValue sentinel is not a client.", this);
                return;
            }
            EnsureAwakeningSubscription();

            if (!_byViewer.TryGetValue(_viewerClientId, out var _markers))
            {
                _markers = new List<Marker>();
                _byViewer[_viewerClientId] = _markers;
            }

            var _marker = new Marker(_iconId, _markedClientId, _viewerClientId, _lifetime);
            if (_markers.Contains(_marker))
            {
                return; // silent dedup
            }

            _markers.Add(_marker);
            PushSliceTo(_viewerClientId);
        }

        /// <summary>Server-side: drop a private marker. An unknown marker is a silent no-op.</summary>
        public void RemoveIcon(ulong _iconId, ulong _markedClientId, ulong _viewerClientId)
        {
            if (!IsServer || IsInvalidClientId(_markedClientId) || IsInvalidClientId(_viewerClientId))
            {
                return;
            }
            if (!_byViewer.TryGetValue(_viewerClientId, out var _markers))
            {
                return;
            }

            int _removed = _markers.RemoveAll(_m => _m.IconId == _iconId && _m.MarkedClientId == _markedClientId);
            if (_removed > 0)
            {
                PushSliceTo(_viewerClientId);
            }
        }

        /// <summary>
        /// Server-side awakening purge: only <see cref="PlayerIconLifetime.ClearAtAwakeningStart"/> markers
        /// are dropped; <see cref="PlayerIconLifetime.Persistent"/> ones survive. Only the viewers whose
        /// slice actually changed are re-pushed.
        /// </summary>
        public void ClearTransientMarkers()
        {
            if (!IsServer)
            {
                return;
            }

            foreach (var _viewerId in _byViewer.Keys.ToList())
            {
                int _removed = _byViewer[_viewerId].RemoveAll(_m => _m.Lifetime == PlayerIconLifetime.ClearAtAwakeningStart);
                if (_removed > 0)
                {
                    PushSliceTo(_viewerId);
                }
            }
        }

        /// <summary>SERVER-ONLY read of the authoritative table (tests / debug). Empty for an unknown viewer.</summary>
        public IReadOnlyList<Marker> GetServerMarkersFor(ulong _viewerClientId) =>
            _byViewer.TryGetValue(_viewerClientId, out var _markers) ? _markers : Array.Empty<Marker>();

        // Rejoin 02: a rejoined player's new connection views as his seat.
        private void OnClientConnected(ulong _clientId) => PushSliceTo(SeatOf(_clientId));

        private void OnClientDisconnected(ulong _clientId)
        {
            // Rejoin 01/02: mid-game the leaver's seat is reserved and he may come back: keep his markers.
            GameManager _game = GameManager.instance;
            if (_game != null && _game.IsSpawned && !_game.IsInLobbyPhase)
            {
                return;
            }
            _byViewer.Remove(SeatOf(_clientId));
        }

        private ulong SeatOf(ulong _clientId)
        {
            CharacterManager _resolved = _characterManager != null ? _characterManager : null;
            return _resolved != null ? _resolved.SeatOfTransport(_clientId) : _clientId;
        }

        // ---- targeted delivery ------------------------------------------------------------------

        /// <summary>
        /// Pushes ONE viewer's slice to that viewer and nobody else. Host short-circuit mirrors
        /// Power.EmitVerdictServer: when the viewer IS the server the slice is applied locally with no RPC;
        /// otherwise GetSafeRpcTarget wraps the target so a simulated bot (clientId >= 100) is intercepted
        /// by the host instead of being sent over the wire.
        /// </summary>
        private void PushSliceTo(ulong _viewerClientId)
        {
            if (!IsServer)
            {
                return;
            }

            IconEntry[] _slice = BuildSlice(_viewerClientId);

            if (_viewerClientId == NetworkManager.ServerClientId)
            {
                ApplySlice(_viewerClientId, _slice);
                return;
            }

            CharacterManager _resolved = ResolveCharacterManager();
            if (_resolved == null)
            {
                // No safe target to wrap: sending an UNWRAPPED RPC would break the bot-debug flow, so the
                // push is dropped — but ResolveCharacterManager has already told the console about it.
                return;
            }
            ReceiveIconSliceRpc(_slice, _viewerClientId, _resolved.GetSafeRpcTarget(_viewerClientId));
        }

        private IconEntry[] BuildSlice(ulong _viewerClientId)
        {
            if (!_byViewer.TryGetValue(_viewerClientId, out var _markers) || _markers.Count == 0)
            {
                return Array.Empty<IconEntry>();
            }

            var _slice = new IconEntry[_markers.Count];
            for (int _i = 0; _i < _markers.Count; _i++)
            {
                _slice[_i] = new IconEntry
                {
                    IconId = _markers[_i].IconId,
                    MarkedClientId = _markers[_i].MarkedClientId,
                    ViewerClientId = _viewerClientId,
                };
            }
            return _slice;
        }

        // The viewer is an EXPLICIT parameter as well as a field of each entry: an EMPTY slice (the last
        // marker of a viewer being removed, or a late joiner with nothing) still has to name the bucket it
        // clears, and an empty array carries no entry to read it from.
        [Rpc(SendTo.SpecifiedInParams)]
        private void ReceiveIconSliceRpc(IconEntry[] _entries, ulong _viewerClientId, RpcParams _params) =>
            ApplySlice(_viewerClientId, _entries);

        private void ApplySlice(ulong _viewerClientId, IconEntry[] _entries)
        {
            if (!_localByViewer.TryGetValue(_viewerClientId, out var _entriesForViewer))
            {
                _entriesForViewer = new List<IconEntry>();
                _localByViewer[_viewerClientId] = _entriesForViewer;
            }

            // Only THIS viewer's bucket is replaced. Clearing everything is what let an intercepted
            // simulated-bot slice erase the host's own icons.
            _entriesForViewer.Clear();
            if (_entries != null)
            {
                _entriesForViewer.AddRange(_entries);
            }
            onLocalIconsChanged?.Invoke();
        }

        // ---- local (this peer's) read slices -----------------------------------------------------

        // This peer's own identity. On the host that is ServerClientId; a simulated bot's slice lives
        // under its own key and is never mistaken for the host's.
        // 0 is ServerClientId — the honest fallback when the NetworkManager is already gone (teardown).
        // Rejoin 02: a rejoined peer views as the seat it plays (CharacterManager.GetLocalClientId).
        private ulong LocalViewerId => _characterManager != null
            ? _characterManager.GetLocalClientId()
            : NetworkManager != null ? NetworkManager.LocalClientId : 0UL;

        /// <summary>Every icon THIS peer may see AS ITSELF, in registration order.</summary>
        public IReadOnlyList<IconEntry> GetLocalIcons() => GetLocalIconsForViewer(LocalViewerId);

        /// <summary>
        /// The slice this peer holds ON BEHALF OF <paramref name="_viewerClientId"/>. On the host that is
        /// either its own slice or an intercepted simulated bot's; on a real client only its own key exists.
        /// </summary>
        public IReadOnlyList<IconEntry> GetLocalIconsForViewer(ulong _viewerClientId) =>
            _localByViewer.TryGetValue(_viewerClientId, out var _entries) ? _entries : Array.Empty<IconEntry>();

        /// <summary>The icon ids THIS peer may see, as itself, on <paramref name="_markedClientId"/>'s thumbnail.</summary>
        public List<ulong> GetLocalIconsFor(ulong _markedClientId) => GetLocalIconsFor(_markedClientId, LocalViewerId);

        /// <summary>The icon ids <paramref name="_viewerClientId"/> may see on <paramref name="_markedClientId"/>'s thumbnail.</summary>
        public List<ulong> GetLocalIconsFor(ulong _markedClientId, ulong _viewerClientId)
        {
            var _result = new List<ulong>();
            foreach (var _entry in GetLocalIconsForViewer(_viewerClientId))
            {
                if (_entry.MarkedClientId == _markedClientId)
                {
                    _result.Add(_entry.IconId);
                }
            }
            return _result;
        }
    }
}
