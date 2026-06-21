using Avatars;
using NUnit.Framework;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// GOLDEN for the pure avatar-visibility policy (<see cref="AvatarVisibilityController.ResolveVisible"/>):
    /// "you only see each other during the day". Board hides everyone; lobby/seated show everyone except the
    /// local first-person body. Pure bool function → asserted with no scene/network setup.
    /// </summary>
    public class AvatarVisibilityPolicyTests
    {
        [Test]
        public void Board_HidesEveryone()
        {
            Assert.IsFalse(AvatarVisibilityController.ResolveVisible(CameraMode.Board, _isOwner: true), "local hidden in Board");
            Assert.IsFalse(AvatarVisibilityController.ResolveVisible(CameraMode.Board, _isOwner: false), "remote hidden in Board");
        }

        [Test]
        public void FreeRoam_ShowsOthers_HidesLocal()
        {
            Assert.IsFalse(AvatarVisibilityController.ResolveVisible(CameraMode.FreeRoam, _isOwner: true), "local hidden (first-person)");
            Assert.IsTrue(AvatarVisibilityController.ResolveVisible(CameraMode.FreeRoam, _isOwner: false), "remote visible in lobby");
        }

        [Test]
        public void Embodied_ShowsOthers_HidesLocal()
        {
            Assert.IsFalse(AvatarVisibilityController.ResolveVisible(CameraMode.Embodied, _isOwner: true), "local hidden (first-person)");
            Assert.IsTrue(AvatarVisibilityController.ResolveVisible(CameraMode.Embodied, _isOwner: false), "remote visible while seated");
        }
    }
}
