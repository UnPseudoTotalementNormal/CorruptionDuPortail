using System.Collections;
using System.Collections.Generic;
using Smartphone;
using GameLogic;
using AudioSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;
using NSubstitute;
using Unity.Cinemachine;
using Board.BoardCameraSystem;

namespace Tests.PlayMode
{
    public class SmartphoneTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _smartphoneGo;
        private SmartphoneController _smartphone;
        
        private class MockApp : SmartphoneApp
        {
            public override SmartphoneApp GetNeighborApp(SmartphoneController.SwipeDirection direction) => null;
            public override void TryOpenPanel() { IsOpen = true; if (canvasGroup != null) { canvasGroup.interactable = true; canvasGroup.blocksRaycasts = true; } }
            public override void TryClosePanel() { IsOpen = false; if (canvasGroup != null) { canvasGroup.interactable = false; canvasGroup.blocksRaycasts = false; } }
        }

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

            // Mock InputManager (Singleton)
            GameObject inputGo = new GameObject("InputManager");
            inputGo.AddComponent<InputManager>();

            // Mock dependencies for SmartphoneController
            GameObject camGo = new GameObject("Camera");
            var boardCam = camGo.AddComponent<BoardCamera>();
            
            _smartphoneGo = new GameObject("Smartphone");
            _smartphoneGo.SetActive(false); // PRE-AWAKE INJECTION
            _smartphone = _smartphoneGo.AddComponent<SmartphoneController>();
            
            // UI Stubs
            var canvasGo = new GameObject("PhoneCanvas");
            canvasGo.transform.SetParent(_smartphoneGo.transform);
            var rect = canvasGo.AddComponent<RectTransform>();
            var group = canvasGo.AddComponent<CanvasGroup>();
            
            var appParent = new GameObject("AppParent");
            appParent.transform.SetParent(_smartphoneGo.transform);

            // App Stubs
            var defaultAppGo = new GameObject("DefaultApp");
            defaultAppGo.transform.SetParent(appParent.transform);
            var defaultApp = defaultAppGo.AddComponent<MockApp>();
            defaultApp.canvasGroupTransform = defaultAppGo.AddComponent<RectTransform>();
            defaultApp.canvasGroup = defaultAppGo.AddComponent<CanvasGroup>();

            // Inject private fields
            ReflectionHelper.SetPrivateField(_smartphone, "openOnCamera", boardCam);
            ReflectionHelper.SetPrivateField(_smartphone, "phoneCanvasTransform", rect);
            ReflectionHelper.SetPrivateField(_smartphone, "phoneCanvasGroup", group);
            ReflectionHelper.SetPrivateField(_smartphone, "defaultApp", defaultApp);
            ReflectionHelper.SetPrivateField(_smartphone, "appParent", appParent.transform);
            
            _smartphoneGo.AddComponent<NetworkObject>();
            _smartphoneGo.SetActive(true);
            _smartphone.GetComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_smartphone);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(InputManager), "instance", null);

            Object.Destroy(_smartphoneGo);
            Object.Destroy(GameObject.Find("Camera"));
            Object.Destroy(GameObject.Find("InputManager"));
            Object.Destroy(_networkManagerGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Smartphone_TryOpenPanel_SetsState()
        {
            _smartphone.IsOpen = false;
            _smartphone.TryOpenPanel();
            
            Assert.IsTrue(_smartphone.IsOpen);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Smartphone_GoToApp_UpdatesCurrentApp()
        {
            var newAppGo = new GameObject("NewApp");
            var newApp = newAppGo.AddComponent<MockApp>();
            newApp.canvasGroupTransform = newAppGo.AddComponent<RectTransform>();
            newApp.canvasGroup = newAppGo.AddComponent<CanvasGroup>();

            bool changed = false;
            _smartphone.onAppChanged += () => changed = true;

            _smartphone.GoToApp(newApp);

            Assert.AreEqual(newApp, _smartphone.currentApp);
            Assert.IsTrue(changed);
            
            Object.Destroy(newAppGo);
            yield return null;
        }
    }
}
