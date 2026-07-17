using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameLogic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// Top-level probe state with a SHORT FullName ("Tests.PlayMode.Desingleton.LoopProbeState", 42
    /// chars). The state-lifecycle RPC (GameManager.CallStateMethodRpc) carries the state type's
    /// FullName in a FixedString64Bytes (61-byte payload); the fixture's own nested DummyGameState
    /// FullName is 63 chars and would throw a truncation ArgumentException on send. Lifecycle no-ops;
    /// the base class fires the onState* events this test records.
    /// </summary>
    public class LoopProbeState : GameState
    {
        public override void StateUpdateClient() { }
        public override void StateUpdateServer() { }
    }

    /// <summary>
    /// 2-NM E2E — a REAL state-machine transition (GameManager.NextGameState → SwitchGameState) observed
    /// on the remote client, beyond the fixture's minimal SetServerIndex driver. The load-bearing fact:
    /// GameManager.OnNetworkSpawn (SetupGameStates) CLONES its GameState SOs per NetworkManager, so each
    /// GM owns its own state instances — the OnEndStateClient/OnStartStateClient lifecycle RPCs
    /// (CallStateMethodRpc, dispatched by type FullName) must resolve and run on the CLIENT's own
    /// instances. StartHost is blind to this whole path: with one NM there is one clone set and no RPC hop.
    ///
    /// The seeded DummyGameStates are swapped (on BOTH live GMs) for LoopProbeState instances because the
    /// RPC's FixedString64Bytes cannot carry the nested fixture type's FullName (see LoopProbeState).
    /// The probe SOs are not destroyed in teardown on purpose: they mirror the per-NM clones
    /// SetupGameStates itself leaks every test, and destroying them while the GMs still Update()
    /// (GetGameState → StateUpdate) between this teardown and the base fixture's would race.
    /// </summary>
    public class GameLoopClientLifecycleTests : MultiClientGameFixture
    {
        [UnityTest]
        public IEnumerator RealStateTransition_RunsClientLifecycleRpcs_OnClientOwnStateClones()
        {
            // --- Given: both GMs re-seeded with probe states (short type name, wired gameManager).
            SwapStates(HostGm);
            SwapStates(ClientGm);
            GameState _hostState0 = HostGm.gameStates.Keys.First();
            GameState _clientState0 = ClientGm.gameStates.Keys.First();
            Assert.AreNotSame(_hostState0, _clientState0,
                "Host and client GameManagers must own DISTINCT per-NM state instances.");

            // Record the ordered client-side lifecycle. All states share the LoopProbeState type, and
            // CallStateMethodRpc dispatches by type FullName (FirstOrDefault), so both the end (old
            // state) and start (new state) legs land on the client's instance[0] — what we listen to.
            var _clientLifecycle = new List<string>();
            _clientState0.onStateEndClient += () => _clientLifecycle.Add("end");
            _clientState0.onStateStartClient += () => _clientLifecycle.Add("start");

            int _traceBaseline = RemoteIndexTrace.Count;

            // --- When: the REAL transition path — loop-machine advance + OnEndStateServer + client
            // end-RPC + index write + OnStartStateServer + client start-RPC.
            HostGm.NextGameState(true);

            // --- Then: the index write crossed the wire (the fixture's recorder)...
            yield return WaitForRemoteTraceCount(_traceBaseline + 1);
            Assert.AreEqual(1, ClientGm.currentGameStateIndex.Value,
                "Client replica's index must converge to the advanced state.");

            // ...and the lifecycle RPCs ran on the CLIENT's own instances, in order: end THEN start.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientLifecycle.Count >= 2,
                5f,
                $"The client lifecycle RPCs never both arrived (got [{string.Join(",", _clientLifecycle)}]).");
            Assert.AreEqual("end", _clientLifecycle[0],
                "The old state's OnEndStateClient must run FIRST on the remote client.");
            Assert.AreEqual("start", _clientLifecycle[1],
                "The new state's OnStartStateClient must run SECOND on the remote client.");
        }

        /// <summary>Replace a live GM's seeded states with LoopProbeState instances (same count, fresh
        /// settings), wiring gameManager so the base lifecycle asserts (OnStartStateServer / Update's
        /// StateUpdateServer) hold. Mirrors what SetupGameStates wires for the fields this path reads.</summary>
        private static void SwapStates(GameManager _gm)
        {
            _gm.gameStates.Clear();
            for (int _i = 0; _i < SeededStateCount; _i++)
            {
                var _state = ScriptableObject.CreateInstance<LoopProbeState>();
                _state.gameManager = _gm;
                _gm.gameStates.Add(_state, new GameStateSettings());
            }
        }
    }
}
