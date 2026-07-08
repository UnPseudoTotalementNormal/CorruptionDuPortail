using System.Collections;
using System.Collections.Generic;
using Characters;
using GameLogic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// Phase 1 (epic-player-leave-stability) — asserts the unified server-side leave pipeline. Drops a
    /// REAL in-process client MID-GAME (roles assigned) over the 2-NetworkManager loopback substrate and
    /// verifies GameManager.HandlePlayerLeft reacts correctly. (Started life as the Phase-0 repro; it was
    /// rewritten in place from characterizing the old fakify+NRE behavior to asserting the new one.)
    ///
    /// ASSERTED PHASE-1 BEHAVIOR (see the assertions + [LEAVE] logs below):
    ///  - GameManager.HandlePlayerLeft (the single OnClientDisconnectCallback subscription) CHAINS the
    ///    leaver mid-game: Character.isChained -> true, WITHOUT the ChainingState card animation, and
    ///    WITHOUT fakifying (ownerClientId stays the real leaver id — never GameValues.FAKE_CLIENT_ID).
    ///    The old OnPlayerDisconnectedServer fakify path + OnPlayerDisconnectedRpc BoardManager.DestroyCard
    ///    NRE are both gone, so the disconnect logs NO server-side error.
    ///  - The leave does NOT yet advance/unblock the waiting game state — that is Phase 2 (state unblock +
    ///    victory re-check). This test still records "index unchanged" so Phase 2 has a baseline to move.
    ///
    /// Substrate constraint (documented, not a gap): this loopback fixture seeds DummyGameStates (the real
    /// RoleAttribution/Awakening states need the full scene graph) and spawns NO ChainingManager, so
    /// HandlePlayerLeft's mid-game branch takes its documented "no ChainingManager wired" fallback —
    /// applying Character.ChainCharacterServer() directly (isChained true) instead of the full role-reveal
    /// chain. "Advance past the lobby into an active state" is approximated by driving the state index off 0
    /// (index 0 is a DummyGameState, not a LobbyState, so HandlePlayerLeft takes the mid-game branch), and
    /// "roles exist" by assigning a real Role to the leaver. The genuine state-stall of a vanished player
    /// (epic 2.3) cannot be reproduced on DummyGameStates and is covered by Phase 5 tests.
    /// </summary>
    public class PlayerLeaveMidGameTests : MultiClientGameFixture
    {
        // The documented benign UTP loopback socket-teardown noise (see fixture TearDown). It is
        // shutdown-window transport noise, not a server-side game-logic error, so it must not
        // pollute the AC2 error verdict.
        private const string BenignUtpSocketNoise = "socket receive requests were marked as failed";

        [UnityTest]
        public IEnumerator RealClient_DropsMidGame_IsChainedByUnifiedPipeline()
        {
            // --- Arrange: past-lobby, roles exist. -----------------------------------------
            ulong _leaverClientId = ClientNm.LocalClientId;
            Assert.AreNotEqual(0UL, _leaverClientId,
                "The real client must have a non-host clientId (expected 1) before we can drop it mid-game.");

            yield return SpawnRealCharacterForClient(_leaverClientId, new Role { roleName = "TestRole-Leaver" });
            Character _leaver = LastSpawnedCharacter;
            Assert.IsNotNull(_leaver, "Leaver Character was not spawned by the fixture helper.");
            Assert.AreEqual(_leaverClientId, _leaver.ownerClientId.Value,
                "Precondition: the leaver Character must be owned by the real client before disconnect.");

            // Drive the state index off its initial value so we are demonstrably "in game", not in
            // the first state. (Best the DummyGameState substrate allows — see class remarks.)
            SetServerIndex(1);
            yield return WaitForRemoteTraceCount(RemoteIndexTrace.Count + 1);
            int _stateIndexBeforeLeave = HostGm.currentGameStateIndex.Value;
            Debug.Log($"[LEAVE] Arranged: leaverClientId={_leaverClientId} role={_leaver.role.roleName} stateIndex={_stateIndexBeforeLeave}");

            // --- Capture server-side errors during the disconnect window (AC2). ------------
            // The PlayMode harness would auto-fail on the first logged error/exception with a
            // generic message; suppress that for the window so we can capture + report the exact
            // hazard and assert on it deterministically at the end.
            var _capturedErrors = new List<string>();
            Application.LogCallback _logHandler = (condition, stackTrace, type) =>
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                {
                    return;
                }
                if (condition != null && condition.Contains(BenignUtpSocketNoise))
                {
                    return; // documented benign transport-teardown noise, not a server-logic error
                }
                _capturedErrors.Add($"{type}: {condition}");
            };

            bool _serverSawDisconnect = false;
            void OnHostSawDisconnect(ulong _id)
            {
                if (_id == _leaverClientId)
                {
                    _serverSawDisconnect = true;
                }
            }

            bool _prevIgnoreFailing = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += _logHandler;
            HostNm.OnClientDisconnectCallback += OnHostSawDisconnect;
            try
            {
                // --- Act: the real client leaves mid-game (the same disconnect path
                // CoexistenceGateTests sequences around — Shutdown() on the second NetworkManager).
                Debug.Log("[LEAVE] Dropping the real client mid-game (ClientNm.Shutdown()).");
                ClientNm.Shutdown();

                // --- AC1: the server processes the disconnect within a BOUNDED frame budget
                // (WaitUntilOrTimeout fails instead of hanging if it never fires).
                yield return NetworkTestHelper.WaitUntilOrTimeout(
                    () => _serverSawDisconnect,
                    5f,
                    "Server never received OnClientDisconnectCallback for the leaver within the frame budget.");

                // Phase 1 side effect: the seat is CHAINED (not fakified). Bounded wait for it to land.
                yield return NetworkTestHelper.WaitUntilOrTimeout(
                    () => _leaver == null || _leaver.isChained.Value,
                    5f,
                    "Server did not chain the leaver within the frame budget (Phase 1 unified pipeline).");

                // The BoardManager NRE rides a deferred (next-tick) SendTo.Everyone RPC, so it
                // surfaces a few frames after the callback. Bounded wait for it to appear so it is
                // captured inside this window — never an infinite wait, and does NOT fail if no
                // error occurs (absence of error is a valid, green characterization).
                float _elapsed = 0f;
                while (_capturedErrors.Count == 0 && _elapsed < 2f)
                {
                    _elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            finally
            {
                HostNm.OnClientDisconnectCallback -= OnHostSawDisconnect;
                Application.logMessageReceived -= _logHandler;
                LogAssert.ignoreFailingMessages = _prevIgnoreFailing;
            }

            // --- Phase-1 assertions (unified pipeline behavior) ---

            // (a) Phase 1: the leaver seat is CHAINED, not fakified. The Character is server-owned, so a
            //     mid-game leave chains it in place (it is not removed/destroyed like a lobby leave).
            Assert.IsNotNull(_leaver,
                "Leaver Character was destroyed by the disconnect (unexpected — mid-game it is chained in place, not removed).");
            Assert.IsTrue(_leaver.isChained.Value,
                "Phase 1: a mid-game leave must CHAIN the leaver (isChained == true) via GameManager.HandlePlayerLeft.");
            Assert.AreEqual(_leaverClientId, _leaver.ownerClientId.Value,
                "Phase 1: chaining must NOT fakify — ownerClientId stays the real leaver id.");
            Assert.AreNotEqual(GameValues.FAKE_CLIENT_ID, _leaver.ownerClientId.Value,
                "Phase 1: the old fakify path is deleted — ownerClientId must never be FAKE_CLIENT_ID.");
            Debug.Log($"[LEAVE] Post-leave: ownerClientId={_leaver.ownerClientId.Value} " +
                      $"isChained={_leaver.isChained.Value} isEliminated={_leaver.isEliminated.Value}");

            // (b) The leave does NOT yet advance/unblock the game state (that is Phase 2). Record the
            //     baseline so Phase 2 has something to move. (The DummyGameState substrate cannot stall
            //     on a specific player either, so this only documents "no advance".)
            Assert.AreEqual(_stateIndexBeforeLeave, HostGm.currentGameStateIndex.Value,
                "Phase 1: the leave chains but does not advance the game state (state unblock is Phase 2).");

            // (c) AC2/AC5: the disconnect must not log a server-side error. The old NRE
            //     (OnPlayerDisconnectedRpc on the null BoardManager) is gone with the fakify path.
            Debug.Log($"[LEAVE] Captured {_capturedErrors.Count} server-side error(s) during the disconnect window.");
            Assert.IsEmpty(_capturedErrors,
                $"[LEAVE] Mid-game disconnect logged {_capturedErrors.Count} server-side error(s) — current NRE hazard:\n" +
                string.Join("\n", _capturedErrors));
        }
    }
}
