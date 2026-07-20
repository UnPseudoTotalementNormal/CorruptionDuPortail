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

        /// <summary>Wire payload of one marker in a viewer's slice (the viewer is implicit — it is the recipient).</summary>
        public struct IconEntry : INetworkSerializable, IEquatable<IconEntry>
        {
            public ulong IconId;
            public ulong MarkedClientId;

            public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
            {
                _serializer.SerializeValue(ref IconId);
                _serializer.SerializeValue(ref MarkedClientId);
            }

            public bool Equals(IconEntry _other) => IconId == _other.IconId && MarkedClientId == _other.MarkedClientId;
        }

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

        // CLIENT-side slice: what THIS peer has been told it may see. On the host this is the host's own
        // slice (plus any simulated-bot slice the host intercepts), exactly like every other bot-debug path.
        private readonly List<IconEntry> _localEntries = new();

        /// <summary>Raised on this peer whenever its own slice changes — the icon-stack views rebuild on it.</summary>
        public event Action onLocalIconsChanged;

        private CharacterManager _characterManager;
        private readonly List<AwakeningState> _subscribedAwakeningStates = new();
        private Action _onAwakeningStartHandler;

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

            // Lane C resolution (sanctioned only inside OnNetworkSpawn). Null-tolerant: a bare harness may
            // have no CharacterManager, and only the server push path dereferences it.
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

                SubscribeToAwakeningStarts(_networkManager);
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

        private void SubscribeToAwakeningStarts(NetworkManager _networkManager)
        {
            GameManager _gameManager = CompositionRoot.For(_networkManager).GameManager;
            if (_gameManager == null)
            {
                return;
            }

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
            if (!IsServer || !_byViewer.TryGetValue(_viewerClientId, out var _markers))
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

        private void OnClientConnected(ulong _clientId) => PushSliceTo(_clientId);

        private void OnClientDisconnected(ulong _clientId) => _byViewer.Remove(_clientId);

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
                ApplySlice(_slice);
                return;
            }

            if (_characterManager == null)
            {
                // No CharacterManager (bare harness): there is no safe target to wrap, so send nothing
                // rather than risk an unwrapped RPC that would break the bot-debug flow.
                return;
            }
            ReceiveIconSliceRpc(_slice, _characterManager.GetSafeRpcTarget(_viewerClientId));
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
                _slice[_i] = new IconEntry { IconId = _markers[_i].IconId, MarkedClientId = _markers[_i].MarkedClientId };
            }
            return _slice;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ReceiveIconSliceRpc(IconEntry[] _entries, RpcParams _params) => ApplySlice(_entries);

        private void ApplySlice(IconEntry[] _entries)
        {
            _localEntries.Clear();
            if (_entries != null)
            {
                _localEntries.AddRange(_entries);
            }
            onLocalIconsChanged?.Invoke();
        }

        // ---- local (this peer's) read slice ------------------------------------------------------

        /// <summary>Every icon THIS peer may see, in registration order.</summary>
        public IReadOnlyList<IconEntry> GetLocalIcons() => _localEntries;

        /// <summary>The icon ids THIS peer may see on <paramref name="_markedClientId"/>'s thumbnail.</summary>
        public List<ulong> GetLocalIconsFor(ulong _markedClientId)
        {
            var _result = new List<ulong>();
            foreach (var _entry in _localEntries)
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
