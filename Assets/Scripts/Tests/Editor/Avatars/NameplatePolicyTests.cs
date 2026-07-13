using Avatars;
using NUnit.Framework;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// Pins the two design rules of the avatar nameplate (Poyo, 2026-07-13): shown for OTHER players ONLY,
    /// and ONLY in first-person. Pure POCO — no Unity types, no scene.
    /// </summary>
    public class NameplatePolicyTests
    {
        [Test]
        public void ShouldShow_OtherPlayerInFirstPerson_True()
        {
            Assert.IsTrue(NameplatePolicy.ShouldShow(isLocalAvatar: false, isFirstPerson: true));
        }

        [Test]
        public void ShouldShow_OwnAvatar_NeverShown_EvenInFirstPerson()
        {
            Assert.IsFalse(NameplatePolicy.ShouldShow(isLocalAvatar: true, isFirstPerson: true));
        }

        [Test]
        public void ShouldShow_OtherPlayerNotFirstPerson_Hidden()
        {
            Assert.IsFalse(NameplatePolicy.ShouldShow(isLocalAvatar: false, isFirstPerson: false));
        }

        [Test]
        public void ShouldShow_OwnAvatarNotFirstPerson_Hidden()
        {
            Assert.IsFalse(NameplatePolicy.ShouldShow(isLocalAvatar: true, isFirstPerson: false));
        }

        [Test]
        public void ResolveLabel_TrimsSurroundingWhitespace()
        {
            Assert.AreEqual("Poyo", NameplatePolicy.ResolveLabel("  Poyo  "));
        }

        [Test]
        public void ResolveLabel_NullOrBlank_CollapsesToEmpty()
        {
            Assert.AreEqual(string.Empty, NameplatePolicy.ResolveLabel(null));
            Assert.AreEqual(string.Empty, NameplatePolicy.ResolveLabel(""));
            Assert.AreEqual(string.Empty, NameplatePolicy.ResolveLabel("   "));
        }
    }
}
