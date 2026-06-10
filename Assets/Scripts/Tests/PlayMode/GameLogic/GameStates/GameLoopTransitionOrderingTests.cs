using System.Collections;
using System.Collections.Generic;
using GameLogic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameLogic.GameStates
{
    /// <summary>
    /// Shared ordered journal for the 2.11a transition characterization. Top-level
    /// so the recording states (which must be top-level — see below) can append.
    /// </summary>
    internal static class TransitionJournal
    {
        public static readonly List<string> Entries = new();
    }

    // TWO DISTINCT, TOP-LEVEL types. Two reasons they cannot be nested:
    //  - CallStateMethodRpc resolves the target state by GetType().FullName, so two
    //    instances of one type would both dispatch to the first instance (note 3).
    //  - DoStateMethodRpc serializes FullName into a FixedString64Bytes; a nested
    //    type name (...OrderingTests+_RecA) overflows 64 bytes. Top-level keeps the
    //    FullName short (Tests.PlayMode.GameLogic.GameStates.RecStateA = 45 bytes).
    internal class RecStateA : GameState
    {
        public override void StateUpdateClient() {}
        public override void StateUpdateServer() {}
        public override void OnStartStateServer() { TransitionJournal.Entries.Add("StartServer:A"); base.OnStartStateServer(); }
        public override void OnEndStateServer()   { TransitionJournal.Entries.Add("EndServer:A");   base.OnEndStateServer(); }
        public override void OnStartStateClient() { TransitionJournal.Entries.Add("StartClient:A"); base.OnStartStateClient(); }
        public override void OnEndStateClient()   { TransitionJournal.Entries.Add("EndClient:A");   base.OnEndStateClient(); }
    }

    internal class RecStateB : GameState
    {
        public override void StateUpdateClient() {}
        public override void StateUpdateServer() {}
        public override void OnStartStateServer() { TransitionJournal.Entries.Add("StartServer:B"); base.OnStartStateServer(); }
        public override void OnEndStateServer()   { TransitionJournal.Entries.Add("EndServer:B");   base.OnEndStateServer(); }
        public override void OnStartStateClient() { TransitionJournal.Entries.Add("StartClient:B"); base.OnStartStateClient(); }
        public override void OnEndStateClient()   { TransitionJournal.Entries.Add("EndClient:B");   base.OnEndStateClient(); }
    }

    /// <summary>
    /// CHARACTERIZATION GOLDEN (Story 2.11a — the sequence net for the 2.11b
    /// GameLoopMachine arithmetic extraction).
    ///
    /// Pins the CURRENT host-local effect ordering of a single N→N+1
    /// <see cref="GameManager.SwitchGameState"/> on the faithful StartHost harness:
    /// the relative order of OnEndStateServer, the OnEndStateClient RPC, the
    /// currentGameStateIndex.Value write (OnValueChanged), OnStartStateServer, and
    /// the OnStartStateClient RPC.
    ///
    /// The *Client halves traverse the reflection RPC dispatch (DoStateMethodRpc →
    /// CallStateMethodRpc, ClientsAndHost). The host receives its own client RPC,
    /// so this journal pins WHATEVER NGO actually does on the host (discovered, not
    /// assumed). 2.11b re-verifies this journal green AFTER the arithmetic moves to
    /// a Domain POCO — the ordering must not move.
    ///
    /// Test-only (NFR1): no production file is touched.
    /// </summary>
    [Category("GameLoopOrdering")]
    public class GameLoopTransitionOrderingTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TransitionJournal.Entries.Clear();

            _networkManagerGo = new GameObject("NetworkManager");
            _networkManager = _networkManagerGo.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _networkManagerGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>(),
                EnableSceneManagement = false
            };

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;

            // isInGameLoop = false on both → NextGameState skips the day-pass /
            // first-loop branches (those arithmetic branches are 2.11b's concern;
            // this story pins the single-transition effect ordering only).
            var stateA = ScriptableObject.CreateInstance<RecStateA>();
            var stateB = ScriptableObject.CreateInstance<RecStateB>();
            _gameManager.gameStates.Add(stateA, new GameStateSettings { isInGameLoop = false });
            _gameManager.gameStates.Add(stateB, new GameStateSettings { isInGameLoop = false });

            _gameManager.GetComponent<NetworkObject>().Spawn();
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_gameManager);

            // Let the spawn-time index-0 start settle, then start clean.
            yield return null;
            yield return null;

            // Pin the OnValueChanged write into the same journal (the listener seam).
            _gameManager.currentGameStateIndex.OnValueChanged += OnIndexChanged;
        }

        private void OnIndexChanged(int previous, int current)
        {
            TransitionJournal.Entries.Add($"IndexChanged:{previous}->{current}");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_gameManager != null)
            {
                _gameManager.currentGameStateIndex.OnValueChanged -= OnIndexChanged;
            }

            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            // Statics survive across PlayMode sessions when domain reload is off.
            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_networkManagerGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Transition_NToNPlus1_EffectOrdering_IsPinned()
        {
            // Clear only the transition window (drop any spawn-time noise).
            TransitionJournal.Entries.Clear();

            Assert.AreEqual(0, _gameManager.currentGameStateIndex.Value, "Precondition: starts at index 0 (state A).");

            _gameManager.NextGameState();

            // Drain in case any callback were ever deferred; on the current code the
            // host dispatches its own client RPC synchronously (see the pinned order).
            for (int i = 0; i < 6; i++) yield return null;

            Assert.AreEqual(1, _gameManager.currentGameStateIndex.Value, "Transition landed on index 1 (state B).");

            // THE PINNED TOTAL ORDER (discovered on the current pre-refactor code,
            // StartHost host==server). The load-bearing facts this golden guards:
            //   1. OnEndStateServer then OnEndStateClient run BEFORE the index write
            //      — the host dispatches its own ClientsAndHost RPC synchronously.
            //   2. the currentGameStateIndex.Value write (OnValueChanged) sits AFTER
            //      both OnEnd halves and BEFORE both OnStart halves — the listener
            //      seam (BoardCameraManager/RoomFog/LightManager) reads the new index
            //      only after the old state has fully ended.
            //   3. OnStartStateServer then OnStartStateClient run AFTER the write.
            // 2.11b must reproduce this exact sequence after the arithmetic moves to
            // a Domain POCO (the adapter keeps OnEnd → write → OnStart).
            var expected = new[]
            {
                "EndServer:A",
                "EndClient:A",
                "IndexChanged:0->1",
                "StartServer:B",
                "StartClient:B",
            };
            CollectionAssert.AreEqual(expected, TransitionJournal.Entries,
                "GameLoopMachine transition effect ordering changed. Observed: " +
                string.Join(" | ", TransitionJournal.Entries));
        }
    }
}
