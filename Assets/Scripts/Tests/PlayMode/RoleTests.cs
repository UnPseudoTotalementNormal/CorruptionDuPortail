using System.Collections;
using Characters;
using Characters.Powers;
using GameLogic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;
using System.Collections.Generic;

namespace Tests.PlayMode
{
    public class RoleTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private Role _testRole;
        private Power _testPower1;
        private Power _testPower2;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _networkManagerGo = new GameObject("NetworkManager");
            _networkManager = _networkManagerGo.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _networkManagerGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>(),
                EnableSceneManagement = false 
            };
            
            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _testRole = new Role();
            
            // Create dummy powers
            GameObject p1Go = new GameObject("Power1");
            p1Go.AddComponent<NetworkObject>();
            _testPower1 = p1Go.AddComponent<Power>();
            
            GameObject p2Go = new GameObject("Power2");
            p2Go.AddComponent<NetworkObject>();
            _testPower2 = p2Go.AddComponent<Power>();

            p1Go.GetComponent<NetworkObject>().Spawn();
            p2Go.GetComponent<NetworkObject>().Spawn();

            _testRole.powers.Add(_testPower1);
            _testRole.powers.Add(_testPower2);

            yield return null; 
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");
            Object.Destroy(_networkManagerGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AwakenRole_WithMinusOneRegen_ResetsToMax()
        {
            _testPower1.maxPowerUse = 3;
            _testPower1.powerUseLeft.Value = 0;
            _testPower1.powerUseRegenPerAwakening = -1;

            _testRole.AwakenRole();

            Assert.AreEqual(3, _testPower1.powerUseLeft.Value, "Power with -1 regen should reset to maxPowerUse");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AwakenRole_WithIncrementalRegen_AddsCorrectAmount()
        {
            _testPower1.maxPowerUse = 5;
            _testPower1.powerUseLeft.Value = 1;
            _testPower1.powerUseRegenPerAwakening = 2;

            _testRole.AwakenRole();

            Assert.AreEqual(3, _testPower1.powerUseLeft.Value, "Power with +2 regen should go from 1 to 3");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AwakenRole_WithIncrementalRegen_ClampsAtMax()
        {
            _testPower1.maxPowerUse = 5;
            _testPower1.powerUseLeft.Value = 4;
            _testPower1.powerUseRegenPerAwakening = 2;

            _testRole.AwakenRole();

            Assert.AreEqual(5, _testPower1.powerUseLeft.Value, "Power regen should not exceed maxPowerUse");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SleepRole_CancelsActivePowers()
        {
            _testPower1.isCurrentlyUsed = true;
            _testPower1.isPassive = false;

            // Setup AudioManager Mock since Cancel calls StopUse which calls AudioManager
            GameObject audioGo = new GameObject("AudioManager");
            audioGo.AddComponent<AudioSystem.GameAudioManager>();

            _testRole.SleepRole();

            Assert.IsFalse(_testPower1.isCurrentlyUsed, "SleepRole should cancel currently used powers");
            
            Object.Destroy(audioGo);
            yield return null;
        }
    }
}
