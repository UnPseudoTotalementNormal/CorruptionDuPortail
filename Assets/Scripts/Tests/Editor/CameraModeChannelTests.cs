using Avatars;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// Pins the <see cref="CameraModeChannel.SeatedFirstPersonLive"/> signal — the flag that gates the card
    /// first-person look-at hover so it never runs while the player is on an overhead board-overview camera
    /// (which would lay the hovered card flat). Pure SO: instantiated with <c>CreateInstance</c>, no scene.
    /// </summary>
    public class CameraModeChannelTests
    {
        private CameraModeChannel _channel;

        [SetUp]
        public void SetUp() => _channel = ScriptableObject.CreateInstance<CameraModeChannel>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_channel);

        [Test]
        public void SeatedFirstPersonLive_DefaultsFalse()
        {
            Assert.IsFalse(_channel.SeatedFirstPersonLive, "A freshly (re)loaded channel must not report a live first-person node.");
        }

        [Test]
        public void SetSeatedFirstPersonLive_True_UpdatesAndFiresOnce()
        {
            int _events = 0;
            bool _last = false;
            _channel.OnSeatedFirstPersonLiveChanged += _v => { _events++; _last = _v; };

            _channel.SetSeatedFirstPersonLive(true);

            Assert.IsTrue(_channel.SeatedFirstPersonLive);
            Assert.AreEqual(1, _events, "Changing the value must fire the event exactly once.");
            Assert.IsTrue(_last, "The event must carry the new value.");
        }

        [Test]
        public void SetSeatedFirstPersonLive_Unchanged_IsNoOpAndSilent()
        {
            int _events = 0;
            _channel.OnSeatedFirstPersonLiveChanged += _ => _events++;

            // Default is false; setting false again must not fire.
            _channel.SetSeatedFirstPersonLive(false);
            Assert.AreEqual(0, _events, "Setting the same value must not fire the event.");

            // Move to true (fires), then set true again (silent).
            _channel.SetSeatedFirstPersonLive(true);
            _channel.SetSeatedFirstPersonLive(true);
            Assert.AreEqual(1, _events, "A repeated identical set must be a no-op.");
        }

        [Test]
        public void SeatedFirstPersonLive_IsIndependentOfMode()
        {
            _channel.SetSeatedFirstPersonLive(true);
            _channel.Set(CameraMode.Embodied);

            Assert.IsTrue(_channel.SeatedFirstPersonLive, "Publishing a mode must not clobber the first-person flag.");
            Assert.AreEqual(CameraMode.Embodied, _channel.Current);
        }
    }
}
