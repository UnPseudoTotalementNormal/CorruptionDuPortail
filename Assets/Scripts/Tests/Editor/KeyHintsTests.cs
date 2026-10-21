using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// The key hints in the corner (mockups touches v2 K5 + B1): only what works at this moment, the menu and the bug
    /// report always, the tablet's arrows once it is open and only towards an existing screen; the key names follow the
    /// player's keyboard, special keys in French.
    /// </summary>
    [Category("KeyHints")]
    public class KeyHintsTests
    {
        private static List<KeyHintAction> For(bool tabletOpen = false, bool previous = false, bool next = false,
            bool speak = false, bool emotes = false, bool bug = false)
        {
            var _hints = new List<KeyHintAction>();
            CorruptionDuPortail.Domain.KeyHints.For(
                new KeyHintContext(true, tabletOpen, previous, next, speak, emotes, true, bug), _hints);
            return _hints;
        }

        [Test]
        public void NightTurn_TabletVoiceMenuAndBug()
        {
            CollectionAssert.AreEqual(
                new[] { KeyHintAction.OpenTablet, KeyHintAction.Speak, KeyHintAction.Menu, KeyHintAction.ReportBug },
                For(speak: true, bug: true));
        }

        [Test]
        public void SeatedVote_AddsTheEmoteWheel()
        {
            CollectionAssert.AreEqual(
                new[] { KeyHintAction.OpenTablet, KeyHintAction.Speak, KeyHintAction.Emotes, KeyHintAction.Menu },
                For(speak: true, emotes: true));
        }

        [Test]
        public void TabletOpenOnItsFirstScreen_CloseAndNextOnly()
        {
            CollectionAssert.AreEqual(
                new[] { KeyHintAction.CloseTablet, KeyHintAction.NextScreen, KeyHintAction.Menu },
                For(tabletOpen: true, next: true));
        }

        [Test]
        public void TabletOpenBetweenTwoScreens_BothArrowsInOrder()
        {
            CollectionAssert.AreEqual(
                new[] { KeyHintAction.CloseTablet, KeyHintAction.PreviousScreen, KeyHintAction.NextScreen, KeyHintAction.Menu },
                For(tabletOpen: true, previous: true, next: true));
        }

        [Test]
        public void TabletClosed_NoArrowsEvenWithNeighbours()
        {
            CollectionAssert.DoesNotContain(For(previous: true, next: true), KeyHintAction.NextScreen);
        }

        [Test]
        public void MayNotSpeak_NoVoiceHint()
        {
            CollectionAssert.DoesNotContain(For(), KeyHintAction.Speak);
        }

        [Test]
        public void Fill_ReplacesThePreviousContent()
        {
            var _hints = new List<KeyHintAction> { KeyHintAction.ReportBug, KeyHintAction.Emotes };
            CorruptionDuPortail.Domain.KeyHints.For(new KeyHintContext(false, false, false, false, false, false, true, false), _hints);
            CollectionAssert.AreEqual(new[] { KeyHintAction.Menu }, _hints);
        }

        [TestCase("Tab", "Tab", "Tab")]
        [TestCase("Escape", "Esc", "Échap")]
        [TestCase("RightArrow", "Right Arrow", "→")]
        [TestCase("LeftArrow", "Left Arrow", "←")]
        [TestCase("F8", "F8", "F8")]
        [TestCase("V", "v", "V")]
        [TestCase("W", "z", "Z")] // AZERTY: the key in QWERTY's W place prints Z
        [TestCase("T", "", "T")]
        [TestCase("Q", null, "Q")]
        public void Label_FrenchSpecialKeysAndLayoutLetters(string keyName, string displayName, string expected)
        {
            Assert.AreEqual(expected, CorruptionDuPortail.Domain.KeyHints.Label(keyName, displayName));
        }
    }
}
