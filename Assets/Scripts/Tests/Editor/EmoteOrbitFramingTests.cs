using Avatars;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// The emote orbit camera's auto-framing measured on the real PlayerAvatar prefab. Framing on every renderer took
    /// the loose skinned bounds of the arms, ears and fur shells: a 41.6 m radius that put the camera behind the cat
    /// on the far side of the table. Framed on the body and head, the camera stays on this side of the far seat.
    /// </summary>
    public class EmoteOrbitFramingTests
    {
        private const string PlayerAvatarPrefabPath = "Assets/Prefabs/Avatars/PlayerAvatar.prefab";
        // GameScene's AvatarManager: the local cat sits 14 m from the table centre, the others 23 m.
        private const float LocalSeatRadius = 14f;
        private const float RemoteSeatRadius = 23f;

        private GameObject _avatar;
        private GameObject _camera;

        [TearDown]
        public void TearDown()
        {
            if (_avatar != null) Object.DestroyImmediate(_avatar);
            if (_camera != null) Object.DestroyImmediate(_camera);
        }

        [Test]
        public void AutoFrame_OnRealCat_StaysBeforeTheFarSeat()
        {
            _avatar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerAvatarPrefabPath));
            _camera = new GameObject("EmoteOrbitCamera");
            AvatarEmoteOrbitCamera _orbit = _camera.AddComponent<AvatarEmoteOrbitCamera>();

            _orbit.MeasureAutoFrame(_avatar.GetComponent<PlayerAvatar>());

            Assert.That(_orbit.AutoDistance, Is.LessThan(LocalSeatRadius + RemoteSeatRadius));
        }

        [Test]
        public void Orbit_StopsInFrontOfAWall()
        {
            _avatar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _avatar.transform.position = new Vector3(0f, 0f, -10f);
            _avatar.transform.localScale = new Vector3(20f, 20f, 1f);
            _camera = new GameObject("EmoteOrbitCamera");
            AvatarEmoteOrbitCamera _orbit = _camera.AddComponent<AvatarEmoteOrbitCamera>();
            Physics.SyncTransforms();

            float _clear = _orbit.ClearRadius(Vector3.zero, Vector3.back, 30f, out Collider _blocker);

            // Wall face at z = -9.5, swept sphere of 1 m: the camera centre stops 8.5 m out.
            Assert.That(_blocker, Is.SameAs(_avatar.GetComponent<Collider>()));
            Assert.That(_clear, Is.EqualTo(8.5f).Within(0.05f));
        }

        [Test]
        public void Orbit_IgnoresTheCatsOwnColliders()
        {
            _avatar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerAvatarPrefabPath));
            _avatar.transform.position = new Vector3(0f, 0f, -10f);
            _camera = new GameObject("EmoteOrbitCamera");
            AvatarEmoteOrbitCamera _orbit = _camera.AddComponent<AvatarEmoteOrbitCamera>();
            Physics.SyncTransforms();

            float _clear = _orbit.ClearRadius(Vector3.zero, Vector3.back, 30f, out Collider _blocker);

            Assert.That(_blocker, Is.Null);
            Assert.That(_clear, Is.EqualTo(30f));
        }

        // The day's rows of cards hang under the rendering camera: they moved with it, and stopping in front of them
        // walked the camera into the cat's head (playtest report 2026-11-13).
        [Test]
        public void Orbit_IgnoresWhatHangsUnderTheRenderingCamera()
        {
            _camera = new GameObject("EmoteOrbitCamera");
            AvatarEmoteOrbitCamera _orbit = _camera.AddComponent<AvatarEmoteOrbitCamera>();
            var _view = new GameObject("MainView");
            _avatar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _avatar.transform.SetParent(_view.transform, false);
            _avatar.transform.position = new Vector3(0f, 0f, -10f);
            _avatar.transform.localScale = new Vector3(20f, 20f, 1f);
            Physics.SyncTransforms();
            try
            {
                float _clear = _orbit.ClearRadius(Vector3.zero, Vector3.back, 30f, out Collider _blocker, _view.transform);

                Assert.That(_blocker, Is.Null);
                Assert.That(_clear, Is.EqualTo(30f));
            }
            finally
            {
                _avatar = null;
                Object.DestroyImmediate(_view);
            }
        }
    }
}
