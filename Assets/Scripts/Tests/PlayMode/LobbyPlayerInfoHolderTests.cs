using System.Collections;
using Characters;
using Network;
using Network.Player;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// Story 13.0 (Epic 13) — proves the LobbyPlayerInfoHolder data backbone behaviour is preserved and the
    /// new runtime-update seam (AC#5) works. Host-only harness (mirrors ChainingManagerTests): a CharacterManager
    /// is spawned alongside the holder so its OnNetworkSpawn resolves a non-null characterManager via the
    /// composition root (the server connect path reaches GetSafeRpcTarget). All assertions are taken relative to
    /// a captured count so the host's own connect-flow entry never makes them flaky.
    /// </summary>
    public class LobbyPlayerInfoHolderTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _characterManagerGo;
        private GameObject _holderGo;
        private LobbyPlayerInfoHolder _holder;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _networkManagerGo = new GameObject("NetworkManager");
            _networkManager = _networkManagerGo.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _networkManagerGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>()
            };
            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            // The holder resolves CharacterManager (lane C) in OnNetworkSpawn; spawn one so the host connect
            // path (AskForPlayerInfo -> GetSafeRpcTarget) has a non-null manager registered for this NM.
            _characterManagerGo = new GameObject("CharacterManager");
            _characterManagerGo.AddComponent<NetworkObject>();
            _characterManagerGo.AddComponent<CharacterManager>();
            _characterManagerGo.GetComponent<NetworkObject>().Spawn();

            _holderGo = new GameObject("LobbyPlayerInfoHolder");
            _holderGo.AddComponent<NetworkObject>();
            _holder = _holderGo.AddComponent<LobbyPlayerInfoHolder>();
            _holderGo.GetComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_holder);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _networkManager == null || !_networkManager.IsListening, 5f,
                "NGO did not stop listening within 5s after Shutdown().");

            Object.Destroy(_holderGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_networkManagerGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AddDebugPlayer_AddsServerEntry_AndGetPlayerInfoFindsIt()
        {
            int before = _holder.playerInfos.Count;
            _holder.AddDebugPlayer(101, "Bot");

            Assert.AreEqual(before + 1, _holder.playerInfos.Count, "AddDebugPlayer should append one entry.");
            Assert.AreEqual("Bot", _holder.GetPlayerInfo(101).playerName.ToString());
            Assert.AreEqual(101ul, _holder.GetPlayerInfo(101).playerClientId);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UpdatePlayerInfo_ReplacesEntryByClientId_NoPhantom()
        {
            _holder.AddDebugPlayer(5, "Alice");
            int before = _holder.playerInfos.Count;

            _holder.UpdatePlayerInfo(new PlayerInfo
            {
                playerClientId = 5,
                playerName = "Alice2",
                playerFullName = "Alice2",
                playerSteamId = 0
            });

            Assert.AreEqual(before, _holder.playerInfos.Count, "Update must replace in place, never add a phantom.");
            Assert.AreEqual("Alice2", _holder.GetPlayerInfo(5).playerName.ToString(), "Entry should be replaced by clientId.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator UpdatePlayerInfo_UnknownClientId_IsNoOp()
        {
            _holder.AddDebugPlayer(5, "Alice");
            int before = _holder.playerInfos.Count;

            _holder.UpdatePlayerInfo(new PlayerInfo
            {
                playerClientId = 999,
                playerName = "Ghost",
                playerFullName = "Ghost",
                playerSteamId = 0
            });

            Assert.AreEqual(before, _holder.playerInfos.Count, "Unknown clientId must be a no-op (no phantom).");
            Assert.AreEqual(default(PlayerInfo), _holder.GetPlayerInfo(999), "Unknown clientId must not resolve.");
            Assert.AreEqual("Alice", _holder.GetPlayerInfo(5).playerName.ToString(), "Existing entry must be untouched.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Disconnect_RemovesEntryByClientId()
        {
            _holder.AddDebugPlayer(5, "Alice");
            int before = _holder.playerInfos.Count;

            // Removal is the private server-side OnClientDisconnected scan (subscribed to the NM disconnect
            // callback in production). Invoke it directly to prove the remove path.
            ReflectionHelper.InvokePrivateMethod(_holder, "OnClientDisconnected", 5ul);

            Assert.AreEqual(before - 1, _holder.playerInfos.Count, "Disconnect should remove the matching entry.");
            Assert.AreEqual(default(PlayerInfo), _holder.GetPlayerInfo(5), "Removed clientId must no longer resolve.");
            yield return null;
        }

        // Code-review F4: exercise the FULL client-helper seam (AC#5), not just the server-side UpdatePlayerInfo.
        // Host is also a client (LocalClient.ClientId == 0); its own entry is added by the connect flow
        // (AskForPlayerInfo → SavePlayerInfoRpc). Wait for it, rename the local profile, then drive
        // UpdateLocalPlayerInfo() → UpdatePlayerInfoServerRpc → server replace. Also pins the F2 authority fix:
        // the server stamps the RPC sender's id, so the host's own entry (0) is the one replaced.
        [UnityTest]
        public IEnumerator UpdateLocalPlayerInfo_DrivesRpcSeam_ReplacesOwnEntryByStampedSenderId()
        {
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => HasEntry(0ul), 5f, "host's own entry (clientId 0) was not added by the connect flow.");

            int before = _holder.playerInfos.Count;
            LocalPlayerInfoHolder.playerInfo = new PlayerInfo
            {
                playerName = "HostRenamed",
                playerFullName = "HostRenamed",
                playerSteamId = 0
            };

            _holder.UpdateLocalPlayerInfo();

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _holder.GetPlayerInfo(0).playerName.ToString() == "HostRenamed", 5f,
                "UpdateLocalPlayerInfo() did not replicate the rename through the RPC seam.");

            Assert.AreEqual(before, _holder.playerInfos.Count, "Seam update must replace in place, not add.");
            Assert.AreEqual(0ul, _holder.GetPlayerInfo(0).playerClientId,
                "Server must stamp the RPC sender's id (host = 0), proving the SenderClientId authority fix.");
            yield return null;
        }

        private bool HasEntry(ulong clientId)
        {
            foreach (var info in _holder.playerInfos)
            {
                if (info.playerClientId == clientId) return true;
            }
            return false;
        }
    }
}
