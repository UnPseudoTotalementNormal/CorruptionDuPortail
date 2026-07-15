using NUnit.Framework;
using Unity.Netcode;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Tests.Editor
{
    /// <summary>
    /// Regression tripwire for investigation join-relay-bind-failure: the joining client inherits
    /// <see cref="NetworkConfig.ConnectionApproval"/> from the serialized BootScene NetworkManager (the client
    /// join path never enables it — only the host does, via <c>ConnectionApprovalGate.Enable</c>). If this
    /// value silently reverts to <c>false</c>, host and client disagree and NGO rejects EVERY connection
    /// request as "Incomplete connection request message given config" → <c>DisconnectClient</c> (all joins
    /// broken). Keep it true.
    /// </summary>
    [Category("Networking")]
    public class ConnectionApprovalConfigGuardTests
    {
        private const string BootScenePath = "Assets/Scenes/BootScene.unity";

        private Scene _scene;
        private bool _opened;
        private bool _wasAlreadyLoaded;

        [SetUp]
        public void OpenBootScene()
        {
            // Don't yank BootScene out of a developer's editor session if they already had it open — additive
            // OpenScene returns the existing handle, so only close in teardown when WE opened it fresh.
            _wasAlreadyLoaded = SceneManager.GetSceneByPath(BootScenePath).isLoaded;
            _scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Additive);
            _opened = _scene.IsValid();
        }

        [TearDown]
        public void CloseBootScene()
        {
            if (_opened && !_wasAlreadyLoaded && _scene.IsValid())
            {
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        [Test]
        public void BootScene_NetworkManager_HasConnectionApprovalEnabled()
        {
            Assert.IsTrue(_opened, $"Could not open {BootScenePath}");

            NetworkManager _networkManager = null;
            foreach (var _root in _scene.GetRootGameObjects())
            {
                _networkManager = _root.GetComponent<NetworkManager>()
                                  ?? _root.GetComponentInChildren<NetworkManager>(true);
                if (_networkManager != null)
                {
                    break;
                }
            }

            Assert.IsNotNull(_networkManager, "NetworkManager not found in BootScene.");
            Assert.IsTrue(
                _networkManager.NetworkConfig.ConnectionApproval,
                "NetworkConfig.ConnectionApproval must stay TRUE — host (ConnectionApprovalGate) and client "
                + "must agree, or NGO rejects every join as 'Incomplete connection request message given "
                + "config' (see investigation join-relay-bind-failure).");
        }
    }
}
