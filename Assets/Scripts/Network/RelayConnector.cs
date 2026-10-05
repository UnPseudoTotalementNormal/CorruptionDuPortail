#region

using System;
using System.Collections.Generic;
using System.Text;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

#endregion

namespace Network
{
    /// <summary>
    /// Single decision point for every Unity Relay connection (investigation vpn-instant-disconnect,
    /// backlog #7). Tries "dtls" (UDP) first and falls back to "wss" (WebSocket/TCP 443) when the failure is
    /// connectivity-shaped (<see cref="RelayFallbackPolicy"/>) — a VPN / captive portal / corporate network
    /// that blocks UDP otherwise makes the game unjoinable, with no reachable setting. The fallback is purely
    /// LOCAL to this peer: Relay supports mixed connection types inside one session (host dtls + client wss,
    /// UTP cross-play doc), so nothing transits through the UGS lobby.
    /// </summary>
    public static class RelayConnector
    {
        private const string LogTag = "[RELAY]";
        private const string DtlsConnectionType = "dtls";
        private const string WssConnectionType = "wss";

        // dtls first (lowest latency), wss as the UDP-hostile-network fallback.
        private static readonly string[] ProtocolOrder = { DtlsConnectionType, WssConnectionType };

        // How long the HOST waits for the relay BIND to reach Established after StartHost before declaring
        // the protocol unreachable. StartHost itself returns true on a merely-local bind, so without this
        // check a host behind a UDP-blocking network would sit forever on a dead dtls allocation.
        private const float HostBindDeadlineSeconds = 10f;

        // Safety cap on the inter-attempt Shutdown drain — NGO completes a Shutdown across frames.
        private const float ShutdownDrainDeadlineSeconds = 5f;

        /// <summary>
        /// Full client connect for a Relay join code: allocation + transport config + StartClient + the shared
        /// <see cref="JoinHandshake"/> wait, retrying on the next protocol when the failure looks like
        /// connectivity. A FRESH join allocation is taken per attempt — Relay's pre-connect idle TTL (~10 s) is
        /// shorter than a 30 s attempt, so the previous attempt's allocation is already dead by retry time. On
        /// definitive failure the player-facing message is built BEFORE any teardown; final teardown itself
        /// stays with the caller's existing catch/AbortJoin path (IsListening-guarded, idempotent).
        /// </summary>
        public static async UniTask<RelayConnectResult> ConnectClientAsync(string _joinCode)
        {
            try
            {
                for (int _i = 0; _i < ProtocolOrder.Length; _i++)
                {
                    string _connectionType = ProtocolOrder[_i];
                    bool _lastAttempt = _i == ProtocolOrder.Length - 1;

                    JoinAllocation _allocation;
                    try
                    {
                        _allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: _joinCode);
                    }
                    catch (RelayServiceException e)
                    {
                        // UGS HTTPS API failure — not UDP-shaped, a protocol change cannot fix it. No retry.
                        Debug.LogError($"{LogTag} Relay join allocation failed: {e.Message}");
                        return RelayConnectResult.Failed(null);
                    }

                    LogServerEndpoints(_allocation.ServerEndpoints);

                    try
                    {
                        if (!TryApplyTransport(_allocation.ToRelayServerData(_connectionType), _connectionType))
                        {
                            return RelayConnectResult.Failed(null); // NetworkManager/transport gone — no session to retry on
                        }
                    }
                    catch (ArgumentException e)
                    {
                        // AllocationUtils throws when the allocation exposes no endpoint for this type. The NEXT
                        // type may still be offered (this is exactly what LogServerEndpoints surfaces) — keep going.
                        Debug.LogError($"{LogTag} No '{_connectionType}' endpoint in the allocation: {e.Message}");
                        if (_lastAttempt)
                        {
                            return RelayConnectResult.Failed(null);
                        }

                        continue;
                    }

                    // NET-02: the profile + build version travel inside the connection request.
                    ClientConnectionPayload.Apply(NetworkManager.Singleton);
                    if (!NetworkManager.Singleton.StartClient())
                    {
                        Debug.LogError($"{LogTag} StartClient refused ('{_connectionType}')");
                        return RelayConnectResult.Failed(null);
                    }

                    ConnectFailReason _fail = await JoinHandshake.WaitForConnectedOrTimeout();
                    if (_fail == ConnectFailReason.None)
                    {
                        if (_connectionType != DtlsConnectionType)
                        {
                            Debug.LogWarning($"{LogTag} Connected via fallback '{_connectionType}' — UDP looks blocked on this network.");
                        }

                        return RelayConnectResult.Succeeded();
                    }

                    // NGO fills DisconnectReason on EVERY transport disconnect (generic "[Disconnect Event]…"
                    // header) — only a genuine server-sent reason may veto the fallback, so route the string
                    // through the pure HasServerReason discriminator instead of a naive empty-check.
                    NetworkManager _nm = NetworkManager.Singleton;
                    bool _hasServerReason = _nm != null && RelayFallbackPolicy.HasServerReason(_nm.DisconnectReason);
                    if (_lastAttempt || _nm == null || !RelayFallbackPolicy.ShouldRetryNextProtocol(_fail, _hasServerReason))
                    {
                        // Build BEFORE the caller's teardown — Shutdown may clear DisconnectReason.
                        return RelayConnectResult.Failed(JoinHandshake.BuildFailureMessage(_fail));
                    }

                    Debug.LogWarning($"{LogTag} '{_connectionType}' attempt failed ({_fail}) — retrying with the next protocol.");

                    // Build the fallback wording NOW: if the drain below stalls, the retry is aborted and this
                    // message is all the player gets — and the drain's own Shutdown may clear DisconnectReason.
                    string _failureMessageBeforeDrain = JoinHandshake.BuildFailureMessage(_fail);

                    // WaitForConnectedOrTimeout's finally dropped the handshake latch; re-latch BEFORE the
                    // inter-attempt Shutdown or ClientDisconnectHandler pops "Connexion à l'hôte perdue"
                    // mid-fallback. The outer finally guarantees the latch never leaks past this method.
                    ClientDisconnectHandler.SetJoinHandshakeInProgress(true);
                    if (!await ShutdownAndDrainAsync())
                    {
                        // NGO never finished shutting down — a retry would StartClient against a listening
                        // NetworkManager and mask the real cause. Fail with the attempt's own wording.
                        return RelayConnectResult.Failed(_failureMessageBeforeDrain);
                    }
                }

                return RelayConnectResult.Failed(null); // unreachable — the last attempt returns above
            }
            finally
            {
                ClientDisconnectHandler.SetJoinHandshakeInProgress(false);
            }
        }

        /// <summary>
        /// Full host bring-up: allocation + transport config + StartHost + relay BIND check, falling back to
        /// wss when dtls never reaches Established. Returns the join code of the SURVIVING allocation — only
        /// fetched after the BIND is Established, so the published code always matches the allocation clients
        /// will reach — or null on failure (the caller's existing error path shuts NGO down).
        /// <see cref="ConnectionApprovalGate.Enable"/> must be called by the caller BEFORE this; it survives
        /// the inter-attempt Shutdown (NetworkConfig + callback, not driver state).
        /// </summary>
        public static async UniTask<string> HostAsync(int _maxConnections)
        {
            for (int _i = 0; _i < ProtocolOrder.Length; _i++)
            {
                string _connectionType = ProtocolOrder[_i];
                bool _lastAttempt = _i == ProtocolOrder.Length - 1;

                Allocation _allocation;
                try
                {
                    _allocation = await RelayService.Instance.CreateAllocationAsync(_maxConnections);
                }
                catch (RelayServiceException e)
                {
                    Debug.LogError($"{LogTag} Relay host allocation failed: {e.Message}");
                    return null;
                }

                LogServerEndpoints(_allocation.ServerEndpoints);

                try
                {
                    if (!TryApplyTransport(_allocation.ToRelayServerData(_connectionType), _connectionType))
                    {
                        return null; // NetworkManager/transport gone — nothing to host on
                    }
                }
                catch (ArgumentException e)
                {
                    // The NEXT connection type may still be offered by a fresh allocation — keep going.
                    Debug.LogError($"{LogTag} No '{_connectionType}' endpoint in the allocation: {e.Message}");
                    if (_lastAttempt)
                    {
                        return null;
                    }

                    continue;
                }

                if (!NetworkManager.Singleton.StartHost())
                {
                    Debug.LogError($"{LogTag} StartHost refused ('{_connectionType}')");
                    return null;
                }

                if (await WaitForRelayBoundAsync(HostBindDeadlineSeconds))
                {
                    try
                    {
                        string _joinCode = await RelayService.Instance.GetJoinCodeAsync(_allocation.AllocationId);
                        if (_connectionType != DtlsConnectionType)
                        {
                            Debug.LogWarning($"{LogTag} Hosting via fallback '{_connectionType}' — UDP looks blocked on this network.");
                        }

                        return _joinCode;
                    }
                    catch (RelayServiceException e)
                    {
                        // Host is up but the code fetch failed — the caller's error path shuts NGO down.
                        Debug.LogError($"{LogTag} GetJoinCode failed: {e.Message}");
                        return null;
                    }
                }

                // A bind failure normally leaves NGO listening (StartHost succeeded, relay unreachable). If it
                // is NOT listening, something ELSE tore the session down (app quit, external Shutdown) — do not
                // fight it by re-allocating and restarting a host nobody asked for.
                NetworkManager _nm = NetworkManager.Singleton;
                if (_nm == null || !_nm.IsListening)
                {
                    Debug.LogWarning($"{LogTag} Session torn down externally during the relay BIND — aborting the host fallback.");
                    return null;
                }

                if (_lastAttempt)
                {
                    Debug.LogError($"{LogTag} Relay BIND failed on every protocol.");
                    return null;
                }

                Debug.LogWarning($"{LogTag} Host relay BIND not Established over '{_connectionType}' within {HostBindDeadlineSeconds}s — retrying with the next protocol.");
                if (!await ShutdownAndDrainAsync())
                {
                    return null; // NGO never finished shutting down — a StartHost retry would be refused anyway
                }
            }

            return null; // unreachable — the last attempt returns above
        }

        // Sets the flag and the server data COHERENTLY: NGO picks WebSocketNetworkInterface from UseWebSockets
        // ALONE — a wss RelayServerData with the flag false is only a LogError, not an auto-correction. Set on
        // EVERY attempt (both directions) so no stale flag leaks into a later attempt or session; the
        // serialized BootScene value stays untouched. False = the NetworkManager or its transport is gone
        // (scene teardown mid-flow) — the caller must fail, not retry.
        private static bool TryApplyTransport(RelayServerData _serverData, string _connectionType)
        {
            NetworkManager _nm = NetworkManager.Singleton;
            UnityTransport _transport = _nm != null ? _nm.GetComponent<UnityTransport>() : null;
            if (_transport == null)
            {
                Debug.LogError($"{LogTag} NetworkManager/UnityTransport unavailable — cannot configure the relay connection.");
                return false;
            }

            _transport.UseWebSockets = _connectionType == WssConnectionType;
            _transport.SetRelayServerData(_serverData);
            return true;
        }

        // Polls the relay BIND status after StartHost. Established = reachable; AllocationInvalid = dead
        // allocation (fail fast); still NotEstablished at the deadline = protocol unreachable.
        private static async UniTask<bool> WaitForRelayBoundAsync(float _deadlineSeconds)
        {
            double _start = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - _start < _deadlineSeconds)
            {
                NetworkManager _nm = NetworkManager.Singleton;
                if (_nm == null || !_nm.IsListening)
                {
                    return false;
                }

                RelayConnectionStatus _status = GetRelayStatus(_nm.GetComponent<UnityTransport>());
                if (_status == RelayConnectionStatus.Established)
                {
                    return true;
                }

                if (_status == RelayConnectionStatus.AllocationInvalid)
                {
                    Debug.LogError($"{LogTag} Relay allocation reported invalid during BIND.");
                    return false;
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            return false;
        }

        // A ref local must not span an await — isolate the driver access in a synchronous helper.
        private static RelayConnectionStatus GetRelayStatus(UnityTransport _transport)
        {
            ref NetworkDriver _driver = ref _transport.GetNetworkDriver();
            return _driver.IsCreated ? _driver.GetRelayConnectionStatus() : RelayConnectionStatus.NotEstablished;
        }

        // NGO completes a Shutdown across frames; the next StartClient/StartHost must not race it. False = the
        // drain deadline expired with NGO still listening (pathological stuck Shutdown) — callers must abort
        // the fallback instead of masking the cause behind a refused Start*.
        private static async UniTask<bool> ShutdownAndDrainAsync()
        {
            NetworkManager _nm = NetworkManager.Singleton;
            if (_nm == null)
            {
                return true;
            }

            if (_nm.IsListening)
            {
                _nm.Shutdown();
            }

            double _start = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - _start < ShutdownDrainDeadlineSeconds)
            {
                _nm = NetworkManager.Singleton;
                if (_nm == null || !_nm.IsListening)
                {
                    return true;
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            Debug.LogWarning($"{LogTag} Shutdown drain exceeded {ShutdownDrainDeadlineSeconds}s — aborting the protocol fallback.");
            return false;
        }

        // One line per allocation: names which connection types Relay actually offers — the wss fallback
        // depends on a wss endpoint existing in prod (playtest checklist, investigation vpn-instant-disconnect).
        private static void LogServerEndpoints(List<RelayServerEndpoint> _endpoints)
        {
            if (_endpoints == null)
            {
                return;
            }

            StringBuilder _sb = new StringBuilder(64);
            for (int _i = 0; _i < _endpoints.Count; _i++)
            {
                if (_i > 0)
                {
                    _sb.Append(", ");
                }

                _sb.Append(_endpoints[_i].ConnectionType);
            }

            Debug.Log($"{LogTag} Allocation endpoints: {_sb}");
        }
    }
}
