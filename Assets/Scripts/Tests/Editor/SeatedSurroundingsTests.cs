using System.Reflection;
using Avatars;
using NUnit.Framework;
using Presentation;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// Board task T11: the room around the table disappears, the seated floor appears and the camera clears to black
    /// only while the seated first-person node is live, and everything comes back exactly as it was when the player
    /// leaves it. No scene: the component's private fields are wired and its OnEnable called by reflection (EditMode
    /// does not run MonoBehaviour messages).
    /// </summary>
    public class SeatedSurroundingsTests
    {
        private CameraModeChannel _channel;
        private GameObject _cameraObject;
        private Camera _camera;
        private GameObject _wall;
        private GameObject _seatedFloor;
        private GameObject _host;
        private SeatedSurroundings _surroundings;

        [SetUp]
        public void SetUp()
        {
            _channel = ScriptableObject.CreateInstance<CameraModeChannel>();
            _cameraObject = new GameObject("Camera");
            _camera = _cameraObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.backgroundColor = Color.blue;
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _seatedFloor = new GameObject("SeatedFloor");
            _seatedFloor.SetActive(false);
            _host = new GameObject("SeatedSurroundings");
            _surroundings = _host.AddComponent<SeatedSurroundings>();
            Set("_cameraMode", _channel);
            Set("_camera", _camera);
            Set("_room", new[] { _wall.GetComponent<Renderer>() });
            Set("_seatedFloor", _seatedFloor);
            Call("OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            Call("OnDisable");
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_wall);
            Object.DestroyImmediate(_seatedFloor);
            Object.DestroyImmediate(_cameraObject);
            Object.DestroyImmediate(_channel);
        }

        [Test]
        public void BoardView_KeepsTheRoom()
        {
            Assert.IsTrue(_wall.GetComponent<Renderer>().enabled);
            Assert.IsFalse(_seatedFloor.activeSelf, "The seated floor only shows in first person.");
            Assert.AreEqual(CameraClearFlags.Skybox, _camera.clearFlags);
        }

        [Test]
        public void SeatedLive_HidesTheRoomShowsTheSeatedFloorAndClearsToBlack()
        {
            _channel.SetSeatedFirstPersonLive(true);

            Assert.IsFalse(_wall.GetComponent<Renderer>().enabled, "The room must be hidden in first person.");
            Assert.IsTrue(_seatedFloor.activeSelf, "The seated floor replaces the room's floor in first person.");
            Assert.AreEqual(CameraClearFlags.SolidColor, _camera.clearFlags);
            Assert.AreEqual(Color.black, _camera.backgroundColor);
        }

        [Test]
        public void LeavingSeated_RestoresTheRoomAndTheCamera()
        {
            _channel.SetSeatedFirstPersonLive(true);
            _channel.SetSeatedFirstPersonLive(false);

            Assert.IsTrue(_wall.GetComponent<Renderer>().enabled);
            Assert.IsFalse(_seatedFloor.activeSelf);
            Assert.AreEqual(CameraClearFlags.Skybox, _camera.clearFlags);
            Assert.AreEqual(Color.blue, _camera.backgroundColor);
        }

        [Test]
        public void Disabled_KeepsTheRoomInFirstPerson()
        {
            _surroundings.Enabled = false;
            _channel.SetSeatedFirstPersonLive(true);

            Assert.IsTrue(_wall.GetComponent<Renderer>().enabled);
            Assert.AreEqual(CameraClearFlags.Skybox, _camera.clearFlags);
        }

        private void Set(string _name, object _value) =>
            typeof(SeatedSurroundings).GetField(_name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_surroundings, _value);

        private void Call(string _name) =>
            typeof(SeatedSurroundings).GetMethod(_name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_surroundings, null);
    }
}
