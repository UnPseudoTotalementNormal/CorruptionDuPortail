using System.Collections;
using Characters;
using GameLogic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// Rejoin 02 (feat/player-rejoin), server + client over real UTP loopback: a real client drops mid-game (seat
    /// reserved), reconnects with a NEW NGO clientId, claims its seat with its session token and gets it back: the
    /// new connection is aliased to the seat both ways, the reservation and "left" are cleared, and the client's local
    /// identity is the seat again (its local character is the original one).
    /// The fixture does not run the connection-approval gate, so the claim / completion the gate drives
    /// (TryClaimReservedSeat at approval, CompleteRejoin at sync) are called directly here.
    /// </summary>
    public class PlayerRejoinTests : MultiClientGameFixture
    {
        private const string BenignUtpSocketNoise = "socket receive requests were marked as failed";

        [UnityTest]
        public IEnumerator DroppedClient_Reconnects_WithItsToken_AndTakesItsSeatBack()
        {
            ulong _seat = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(_seat, new Role { roleName = "TestRole-Rejoiner" });
            Character _seatCharacter = LastSpawnedCharacter;
            HostCm.Seats.IssueToken(_seat, "rejoin-test-token");
            SetServerIndex(1); // mid-game (index 0 is not a LobbyState, the leave takes the mid-game branch)
            yield return WaitForRemoteTraceCount(RemoteIndexTrace.Count + 1);

            bool _prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true; // documented UTP loopback teardown noise on the drop
            try
            {
                // --- the client drops: its seat is reserved, not chained ---
                ClientNm.Shutdown();
                yield return NetworkTestHelper.WaitUntilOrTimeout(() => HostGm.IsSeatReserved(_seat), 5f,
                    "The dropped client's seat was never reserved.");
                yield return NetworkTestHelper.WaitUntilOrTimeout(() => !ClientNm.ShutdownInProgress && !ClientNm.IsListening, 5f,
                    "The client NetworkManager never finished shutting down.");
                Assert.IsFalse(_seatCharacter.isChained.Value, "A reserved seat is not chained.");

                // --- it reconnects: NGO gives it a new clientId ---
                Assert.IsTrue(ClientNm.StartClient(), "The client could not start again.");
                yield return NetworkTestHelper.WaitUntilOrTimeout(
                    () => ClientNm.IsConnectedClient && CharacterManager.For(ClientNm) != null && GameManager.For(ClientNm) != null, 10f,
                    "The reconnecting client never got its replicas.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = _prevIgnore;
            }

            ulong _connection = ClientNm.LocalClientId;
            Assert.AreNotEqual(_seat, _connection, "Precondition: a reconnect gets a NEW clientId.");
            CharacterManager _clientCm = CharacterManager.For(ClientNm);

            // --- approval (token) then end of sync, as the gate drives them ---
            Assert.IsFalse(HostGm.TryClaimReservedSeat("wrong-token", _connection, out _), "A wrong token opens nothing.");
            Assert.IsTrue(HostGm.TryClaimReservedSeat("rejoin-test-token", _connection, out ulong _claimed), "The seat's token must open it.");
            Assert.AreEqual(_seat, _claimed);
            Assert.IsTrue(HostGm.IsRejoining(_connection));
            HostGm.CompleteRejoin(_connection);

            // Server side: aliased both ways, reservation and "left" gone.
            Assert.AreEqual(_seat, HostCm.SeatOfTransport(_connection), "Messages from the new connection act for the seat.");
            Assert.AreEqual(_connection, HostCm.TransportOfSeat(_seat), "Messages for the seat go to the new connection.");
            Assert.IsFalse(HostGm.IsSeatReserved(_seat), "The seat is no longer reserved.");
            Assert.IsFalse(HostGm.HasClientLeft(_seat), "The player is back: no longer skipped, votes again.");
            Assert.IsFalse(HostGm.IsRejoining(_connection));

            // Client side: it plays its seat again.
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _clientCm.GetLocalClientId() == _seat, 5f,
                "The rejoined client was never told which seat it plays.");
            Character _local = _clientCm.GetLocalCharacter(false);
            Assert.IsNotNull(_local, "The rejoined client must find its own character.");
            Assert.AreEqual(_seat, _local.ownerClientId.Value, "Its local character is the original seat's.");

            // The seat survives the expiry check: a rejoined player is never chained by the grace timer.
            HostGm.ExpireReservedSeats(Time.realtimeSinceStartupAsDouble + HostGm.RejoinGraceSeconds + 1.0);
            yield return null;
            Assert.IsFalse(_seatCharacter.isChained.Value, "A taken-back seat must not be chained when the grace delay ends.");
        }

        // A player relaunches faster than the host notices his crash: his old connection still holds the seat. The
        // token proves it is him: the old connection is dropped (the seat gets reserved) and the new one claims it.
        [UnityTest]
        public IEnumerator FastRelaunch_TokenTakesOverASeatStillHeldByTheDeadConnection()
        {
            ulong _seat = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(_seat, new Role { roleName = "TestRole-FastRelaunch" });
            Character _seatCharacter = LastSpawnedCharacter;
            HostCm.Seats.IssueToken(_seat, "fast-relaunch-token");
            SetServerIndex(1); // mid-game
            yield return WaitForRemoteTraceCount(RemoteIndexTrace.Count + 1);
            Assert.IsFalse(HostGm.IsSeatReserved(_seat), "Precondition: the host still sees the old connection.");

            const ulong _newConnection = 42; // the relaunched game's connection, still being approved (< 100: ids from 100 are bots)
            bool _prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true; // the dropped loopback client logs its own disconnect
            bool _claimed;
            ulong _claimedSeat;
            try
            {
                Assert.IsFalse(HostGm.TryClaimReservedSeat("wrong-token", _newConnection, out _), "A wrong token takes nothing over.");
                _claimed = HostGm.TryClaimReservedSeat("fast-relaunch-token", _newConnection, out _claimedSeat);
                yield return NetworkTestHelper.WaitUntilOrTimeout(() => !ClientNm.IsConnectedClient, 5f,
                    "The old connection was never dropped.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = _prevIgnore;
            }

            Assert.IsTrue(_claimed, "The token must take the seat over from the dead connection.");
            Assert.AreEqual(_seat, _claimedSeat);
            Assert.IsTrue(HostGm.IsSeatReserved(_seat), "Until the new game finishes loading, the seat is reserved.");
            Assert.IsTrue(HostGm.IsRejoining(_newConnection));
            Assert.AreEqual(_seat, HostCm.SeatOfTransport(_newConnection), "The new connection already acts for the seat.");
            Assert.IsFalse(_seatCharacter.isChained.Value, "A taken-over seat is not chained.");
        }
    }
}
