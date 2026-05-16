using System.Collections;
using Network.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public class LobbyManagerTests
    {
        private GameObject _go;
        private LobbyManager _lobbyManager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _go = new GameObject("LobbyManager");
            _lobbyManager = _go.AddComponent<LobbyManager>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CreateLobby_EmptyName_TriggersError()
        {
            string errorReceived = "";
            _lobbyManager.OnLobbyError += (err) => errorReceived = err;

            var settings = new LobbyCreationSettings { lobbyName = "" };
            var task = _lobbyManager.CreateLobby(settings);
            
            // Should fail validation before even trying async cloud call
            Assert.AreEqual("Le nom du lobby ne peut pas être vide", errorReceived);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CreateLobby_InvalidPlayerCount_TriggersError()
        {
            string errorReceived = "";
            _lobbyManager.OnLobbyError += (err) => errorReceived = err;

            var settings = new LobbyCreationSettings { lobbyName = "ValidName", maxPlayers = 1 };
            var task = _lobbyManager.CreateLobby(settings);
            
            Assert.AreEqual("Le nombre de joueurs doit être entre 2 et 20", errorReceived);
            yield return null;
        }
    }
}
