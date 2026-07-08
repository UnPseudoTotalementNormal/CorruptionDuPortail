using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CorruptionDuPortail.Domain;
using Network.Liveness;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// [LIVENESS B2] Orchestration coverage for <see cref="LivenessService"/> (arch-liveness-heartbeat
    /// §8.3/§8.4) — the biggest previously-untested surface. Exercises the SERVER role end to end on the
    /// 2-NM loopback fixture (the service is NGO-bound: it keys its enrollment and its bridge-sink registry off
    /// a live <see cref="NetworkManager"/>), but drives ALL timing through <see cref="ManualLivenessPump"/> —
    /// no wall-clock, no <c>WaitForSeconds</c>. Only the enroll/heartbeat/remove/stop plumbing is "real"; the
    /// tick→decision layer is covered exhaustively in the EditMode LivenessTracker battery.
    ///
    /// The injected <c>pumpOverride</c> only SUPPRESSES the production realtime loop (the service calls
    /// Start()/Stop() on it); the service's OWN tracker is advanced by a SEPARATE
    /// <c>ManualLivenessPump(service.Tracker)</c>.
    /// </summary>
    [Category("Desingleton")]
    public class LivenessServiceOrchestrationTests : MultiClientGameFixture
    {
        // A throwaway pump wrapping a tracker it never ticks — passed as pumpOverride purely to keep the
        // production LivenessNetworkPump (and its real wall-clock loop) out of the test.
        private static ILivenessPump SuppressPump(LivenessConfig _config) =>
            new ManualLivenessPump(new LivenessTracker(_config.Threshold));

        private static void InvokePrivate(LivenessService _service, string _name, ulong _clientId)
        {
            MethodInfo _method = typeof(LivenessService).GetMethod(_name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(_method, $"LivenessService.{_name} not found (renamed?).");
            _method.Invoke(_service, new object[] { _clientId });
        }

        private static bool SinkRegisteredFor(NetworkManager _nm)
        {
            FieldInfo _field = typeof(LivenessNetworkBridge).GetField("s_sinks",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(_field, "LivenessNetworkBridge.s_sinks not found (renamed?).");
            var _sinks = (IDictionary<NetworkManager, ILivenessSink>)_field.GetValue(null);
            return _sinks.ContainsKey(_nm);
        }

        [UnityTest]
        public IEnumerator StartServer_EnrollmentRules_RealClientEnrolled_HostAndBotExcluded()
        {
            var _config = LivenessConfig.Default;
            var _lost = new List<ulong>();
            LivenessService _service = LivenessService.StartServer(
                HostNm, _config, new StopwatchLivenessClock(), id => _lost.Add(id), SuppressPump(_config));
            try
            {
                // The real client is already connected, so StartServer's initial enrollment sweep tracked it
                // (the SAME code the OnClientConnectedCallback runs). The host never heartbeats itself.
                ulong _realClientId = ClientNm.LocalClientId;
                Assert.AreNotEqual(0UL, _realClientId, "The real client must have a non-host clientId.");
                Assert.IsTrue(_service.Tracker.IsTracked(_realClientId),
                    "A connected real client must be enrolled by the server role.");
                Assert.IsFalse(_service.Tracker.IsTracked(HostNm.LocalClientId),
                    "The host's own clientId must NEVER be enrolled (it never heartbeats itself).");

                // Drive the connect-callback enroll guard directly for a fresh real id, the host id, and a bot.
                InvokePrivate(_service, "EnrollRealClient", 7UL);
                Assert.IsTrue(_service.Tracker.IsTracked(7UL), "A real (< 100) client id must enroll.");

                InvokePrivate(_service, "EnrollRealClient", HostNm.LocalClientId);
                Assert.IsFalse(_service.Tracker.IsTracked(HostNm.LocalClientId),
                    "EnrollRealClient must reject the host's own clientId.");

                InvokePrivate(_service, "EnrollRealClient", 100UL);
                Assert.IsFalse(_service.Tracker.IsTracked(100UL),
                    "A simulated bot (>= 100) must NEVER be enrolled.");
            }
            finally
            {
                _service.Stop();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator LivenessService_Heartbeat_Reset_RemovePeer_And_Stop_Teardown()
        {
            var _config = LivenessConfig.Default;
            int _threshold = _config.Threshold;
            var _lost = new List<ulong>();
            LivenessService _service = LivenessService.StartServer(
                HostNm, _config, new StopwatchLivenessClock(), id => _lost.Add(id), SuppressPump(_config));

            // Advance the SERVICE's real tracker deterministically — never wall-clock.
            var _driver = new ManualLivenessPump(_service.Tracker);
            try
            {
                Assert.IsTrue(_service.Pump.IsRunning, "The pump must be running after StartServer.");
                Assert.IsTrue(SinkRegisteredFor(HostNm), "StartServer must register the service as the NM's sink.");

                // Clear the auto-enrolled real client so the tracker is a clean, synthetic slate for driving.
                InvokePrivate(_service, "RemovePeer", ClientNm.LocalClientId);
                Assert.AreEqual(0, _service.Tracker.TrackedCount,
                    "Precondition: tracker cleared to a deterministic slate.");

                // --- OnClientHeartbeat(id) forwards into tracker.RecordBeat (resets that peer's miss counter). ---
                const ulong _beater = 7;
                InvokePrivate(_service, "EnrollRealClient", _beater);
                _driver.AdvanceBeats(_threshold - 1);   // one beat short of eviction
                Assert.IsEmpty(_lost, "No peer may be lost before the threshold.");
                _service.OnClientHeartbeat(_beater);    // a received beat resets the counter to 0
                _driver.AdvanceBeats(1);                // without the reset this tick would have evicted it
                Assert.IsEmpty(_lost,
                    "A heartbeat must reset the miss counter — the beaten peer survives the next tick.");
                Assert.IsTrue(_service.Tracker.IsTracked(_beater), "The beaten peer must remain tracked.");
                InvokePrivate(_service, "RemovePeer", _beater); // done with the beater; clear it

                // --- RemovePeer on disconnect stops a later PeerLost (voluntary leave → no late eviction). ---
                const ulong _leaver = 9;
                InvokePrivate(_service, "EnrollRealClient", _leaver);
                InvokePrivate(_service, "RemovePeer", _leaver);
                Assert.IsFalse(_service.Tracker.IsTracked(_leaver), "RemovePeer must drop the peer.");
                _driver.AdvanceBeats(_threshold * 2);
                Assert.IsEmpty(_lost, "A removed peer must never emit a late PeerLost.");

                // --- Stop() stops the pump AND unregisters the sink (the pump-outlives-session leak lock). ---
                _service.Stop();
                Assert.IsFalse(_service.Pump.IsRunning, "Stop() must stop the pump.");
                Assert.IsFalse(SinkRegisteredFor(HostNm), "Stop() must unregister the NM's sink.");
            }
            finally
            {
                _service.Stop(); // idempotent — safe even after the in-body Stop
            }
            yield return null;
        }
    }
}
