#region

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Characters;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using GameLogic;
using GameLogic.GameStates;
using Network.Player;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Network
{
    /// <summary>
    /// NET-00 (epic-network-sync-hardening): detection-only desync tripwire. At quiet points (shortly after each
    /// game-state change, and periodically) the server sends every remote client the per-component hashes of its
    /// PUBLIC replicated state. Each client waits a few network ticks (NGO delivers RPCs before NetworkVariable
    /// deltas), compares its own projection, re-checks against a fresh server digest on a mismatch, and only a
    /// mismatch that persists across both checks is logged as <c>[DESYNC]</c> — on the client AND on the host (via
    /// a report RPC), each with both projections. Never alters gameplay.
    ///
    /// Also hosts two cheap tripwires evaluated at every check, on every peer: <c>[ROLE]</c> (a real character with
    /// no role once the game started) and <c>[ROSTER]</c> (duplicate roster row, or a real character without a
    /// roster entry once the game started). Each fires once per subject.
    /// </summary>
    public class DesyncMonitor : NetworkBehaviour
    {
        private const string LogTag = "[DESYNC]";
        private const int ClientSettleTicks = 3;
        private const float SettleDelaySeconds = 1f;
        private const float PeriodicCheckSeconds = 15f;

        // Lane A (scene-wired in GameScene, memory: never resolve a peer manager in OnNetworkSpawn).
        [SerializeField] private GameManager gameManager;
        [SerializeField] private CharacterManager characterManager;
        [SerializeField] private LobbyPlayerInfoHolder lobbyPlayerInfoHolder;

        // Test seam: replaces this peer's projection source (2-NM tests inject a divergence). Null in production.
#pragma warning disable 0649
        private Func<PublicStateProjection> _projectionOverride;
#pragma warning restore 0649

        private readonly DesyncRecheckPolicy _policy = new();
        private readonly HashSet<string> _trippedOnce = new(StringComparer.Ordinal);
        private CancellationTokenSource _scheduledCheck;
        private float _nextPeriodicCheck;
        private string _pendingStateKey;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && gameManager != null)
            {
                gameManager.currentGameStateIndex.OnValueChanged += OnServerStateChanged;
            }
            _nextPeriodicCheck = Time.realtimeSinceStartup + PeriodicCheckSeconds;
        }

        public override void OnNetworkDespawn()
        {
            if (gameManager != null)
            {
                gameManager.currentGameStateIndex.OnValueChanged -= OnServerStateChanged;
            }
            CancelScheduledCheck();
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || Time.realtimeSinceStartup < _nextPeriodicCheck)
            {
                return;
            }
            _nextPeriodicCheck = Time.realtimeSinceStartup + PeriodicCheckSeconds;
            RequestCheckServer();
        }

        private void OnServerStateChanged(int _previous, int _current)
        {
            CancelScheduledCheck();
            _scheduledCheck = new CancellationTokenSource();
            ScheduleCheckAsync(_scheduledCheck.Token).Forget();
        }

        private async UniTaskVoid ScheduleCheckAsync(CancellationToken _token)
        {
            bool _canceled = await UniTask.Delay(TimeSpan.FromSeconds(SettleDelaySeconds), cancellationToken: _token)
                .SuppressCancellationThrow();
            if (!_canceled && this != null && IsSpawned)
            {
                RequestCheckServer();
            }
        }

        private void CancelScheduledCheck()
        {
            _scheduledCheck?.Cancel();
            _scheduledCheck?.Dispose();
            _scheduledCheck = null;
        }

        /// <summary>Server-side: run the tripwires locally and send the current digest to every remote client.</summary>
        public void RequestCheckServer()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }
            RunTripwires();
            ReceiveDigestRpc(BuildPayload());
        }

        private DigestPayload BuildPayload()
        {
            Dictionary<string, ulong> _hashes = DesyncDigest.ComputeComponentHashes(BuildProjection());
            var _names = new string[_hashes.Count];
            var _values = new ulong[_hashes.Count];
            int _i = 0;
            foreach (var _pair in _hashes)
            {
                _names[_i] = _pair.Key;
                _values[_i] = _pair.Value;
                _i++;
            }
            return new DigestPayload { stateKey = CurrentStateKey(), names = _names, hashes = _values };
        }

        private PublicStateProjection BuildProjection()
        {
            return _projectionOverride != null
                ? _projectionOverride()
                : PublicStateProjectionBuilder.Build(lobbyPlayerInfoHolder, characterManager, gameManager);
        }

        private string CurrentStateKey()
        {
            if (gameManager == null || !gameManager.IsSpawned)
            {
                return "none";
            }
            return string.Concat(
                gameManager.gameLoopCount.ToString(CultureInfo.InvariantCulture), ":",
                gameManager.currentGameStateIndex.Value.ToString(CultureInfo.InvariantCulture));
        }

        // ---- client side ------------------------------------------------------------------------------------

        [Rpc(SendTo.NotServer)]
        private void ReceiveDigestRpc(DigestPayload _payload)
        {
            CompareAfterSettleAsync(_payload, false).Forget();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void ReceiveRecheckRpc(DigestPayload _payload, RpcParams _params = default)
        {
            CompareAfterSettleAsync(_payload, true).Forget();
        }

        private async UniTaskVoid CompareAfterSettleAsync(DigestPayload _payload, bool _isRecheck)
        {
            int _targetTick = NetworkManager.LocalTime.Tick + ClientSettleTicks;
            bool _canceled = await UniTask.WaitUntil(
                    () => this == null || !IsSpawned || NetworkManager.LocalTime.Tick >= _targetTick,
                    cancellationToken: this.GetCancellationTokenOnDestroy())
                .SuppressCancellationThrow();
            if (_canceled || this == null || !IsSpawned)
            {
                return;
            }

            RunTripwires();

            PublicStateProjection _local = BuildProjection();
            List<string> _mismatches = DesyncDigest.Mismatches(_payload.ToDictionary(), DesyncDigest.ComputeComponentHashes(_local));

            if (!_isRecheck)
            {
                _pendingStateKey = _payload.stateKey;
                if (_policy.OnFirstCheck(_mismatches))
                {
                    RequestRecheckServerRpc();
                }
                return;
            }

            foreach (string _component in _policy.OnRecheck(_mismatches, _pendingStateKey ?? _payload.stateKey))
            {
                string _dump = DesyncDigest.Describe(_local, _component);
                Debug.LogError($"{LogTag} component={_component} state={_payload.stateKey} client={NetworkManager.LocalClientId} " +
                               $"persistent mismatch with the server.\nclient projection:\n{_dump}");
                ReportDesyncServerRpc(_component, _dump, _payload.stateKey);
            }
        }

        // ---- server side ------------------------------------------------------------------------------------

        [Rpc(SendTo.Server)]
        private void RequestRecheckServerRpc(RpcParams _params = default)
        {
            ulong _sender = _params.Receive.SenderClientId;
            if (characterManager != null)
            {
                ReceiveRecheckRpc(BuildPayload(), characterManager.GetSafeRpcTarget(_sender));
            }
            else
            {
                ReceiveRecheckRpc(BuildPayload(), RpcTarget.Single(_sender, RpcTargetUse.Temp));
            }
        }

        [Rpc(SendTo.Server)]
        private void ReportDesyncServerRpc(string _component, string _clientDump, string _stateKey, RpcParams _params = default)
        {
            PublicStateProjection _host = BuildProjection();
            Debug.LogError($"{LogTag} component={_component} state={_stateKey} client={_params.Receive.SenderClientId} " +
                           $"reported a persistent mismatch.\nhost projection:\n{DesyncDigest.Describe(_host, _component)}" +
                           $"\nclient projection:\n{_clientDump}");
        }

        // ---- tripwires (every peer) -------------------------------------------------------------------------

        private void RunTripwires()
        {
            CheckRosterDuplicates();

            if (gameManager == null || !gameManager.IsSpawned || characterManager == null || !characterManager.IsSpawned)
            {
                return;
            }
            if (!IsGameStarted())
            {
                return;
            }

            foreach (Character _character in characterManager.GetCharacters(false))
            {
                if (_character == null || _character.isFake)
                {
                    continue;
                }
                ulong _owner = _character.ownerClientId.Value;

                if (_character.role == null || _character.role.roleName.IsEmpty)
                {
                    TripOnce($"role:{_owner}", $"[ROLE] character={_owner} has no role on peer {NetworkManager.LocalClientId} after the game started.");
                }

                if (lobbyPlayerInfoHolder != null && lobbyPlayerInfoHolder.IsSpawned && !HasRosterEntry(_owner))
                {
                    TripOnce($"missing:{_owner}", $"[ROSTER] missing clientId={_owner} on peer {NetworkManager.LocalClientId} (no pseudo can be shown).");
                }
            }
        }

        private bool IsGameStarted()
        {
            int _index = gameManager.currentGameStateIndex.Value;
            if (gameManager.gameStates.Count == 0 || _index < 0 || _index >= gameManager.gameStates.Count)
            {
                return false;
            }
            GameState _state = gameManager.GetGameState(_index);
            return !(_state is LobbyState) && !(_state is RoleAttributionState);
        }

        private void CheckRosterDuplicates()
        {
            if (lobbyPlayerInfoHolder == null || !lobbyPlayerInfoHolder.IsSpawned || lobbyPlayerInfoHolder.playerInfos == null)
            {
                return;
            }
            var _seen = new HashSet<ulong>();
            foreach (PlayerInfo _info in lobbyPlayerInfoHolder.playerInfos)
            {
                if (!_seen.Add(_info.playerClientId))
                {
                    TripOnce($"dup:{_info.playerClientId}",
                        $"[ROSTER] duplicate clientId={_info.playerClientId} in the roster replica of peer {NetworkManager.LocalClientId}.");
                }
            }
        }

        private bool HasRosterEntry(ulong _clientId)
        {
            foreach (PlayerInfo _info in lobbyPlayerInfoHolder.playerInfos)
            {
                if (_info.playerClientId == _clientId)
                {
                    return true;
                }
            }
            return false;
        }

        private void TripOnce(string _key, string _message)
        {
            if (_trippedOnce.Add(_key))
            {
                Debug.LogError(_message);
            }
        }

        // ---- wire payload -----------------------------------------------------------------------------------

        public struct DigestPayload : INetworkSerializable
        {
            public string stateKey;
            public string[] names;
            public ulong[] hashes;

            public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
            {
                stateKey ??= string.Empty;
                _serializer.SerializeValue(ref stateKey);

                int _count = names?.Length ?? 0;
                _serializer.SerializeValue(ref _count);
                if (_serializer.IsReader)
                {
                    names = new string[_count];
                    hashes = new ulong[_count];
                }
                for (int _i = 0; _i < _count; _i++)
                {
                    _serializer.SerializeValue(ref names[_i]);
                    _serializer.SerializeValue(ref hashes[_i]);
                }
            }

            public Dictionary<string, ulong> ToDictionary()
            {
                var _result = new Dictionary<string, ulong>(StringComparer.Ordinal);
                for (int _i = 0; _i < (names?.Length ?? 0); _i++)
                {
                    _result[names[_i]] = hashes[_i];
                }
                return _result;
            }
        }
    }
}
