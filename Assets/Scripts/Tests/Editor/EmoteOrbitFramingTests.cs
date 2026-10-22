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
    }
}
