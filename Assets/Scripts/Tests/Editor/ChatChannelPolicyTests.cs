using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Story 11.3 — EditMode characterization of <see cref="ChatChannelPolicy"/>, the pure extraction of
    /// ChatManager's channel routing / visibility rules. Pins the deduction-sacred decisions: a message is
    /// visible only on a discovered channel; a channel can be activated only once discovered; sends reject
    /// empty text and the read-only server channel; undiscovering the active channel falls back to general;
    /// and the three-way window-name policy (override wins even if empty, else enum name, else "Chat {id}").
    /// </summary>
    [Category("ChatChannelPolicy")]
    public class ChatChannelPolicyTests
    {
        private const int General = 0;
        private const int Server = -1;
        private readonly ChatChannelPolicy _policy = new();

        private static ISet<int> Discovered(params int[] ids) => new HashSet<int>(ids);

        [Test]
        public void Message_OnDiscoveredChannel_IsVisible()
        {
            Assert.IsTrue(_policy.IsMessageVisible(7, Discovered(General, 7)));
        }

        [Test]
        public void Message_OnUndiscoveredChannel_IsHidden()
        {
            Assert.IsFalse(_policy.IsMessageVisible(7, Discovered(General)));
        }

        [Test]
        public void Channel_CanActivate_OnlyWhenDiscovered()
        {
            Assert.IsTrue(_policy.CanActivateChannel(7, Discovered(General, 7)));
            Assert.IsFalse(_policy.CanActivateChannel(7, Discovered(General)));
        }

        [Test]
        public void Send_RejectsEmptyOrNullText()
        {
            Assert.IsFalse(_policy.CanSendMessage("", General, Server));
            Assert.IsFalse(_policy.CanSendMessage(null, General, Server));
        }

        [Test]
        public void Send_RejectsTheServerChannel()
        {
            Assert.IsFalse(_policy.CanSendMessage("hello", Server, Server));
        }

        [Test]
        public void Send_AllowsNonEmptyTextOnANonServerChannel()
        {
            Assert.IsTrue(_policy.CanSendMessage("hello", General, Server));
        }

        [Test]
        public void Undiscover_FallsBackToGeneral_OnlyWhenTheUndiscoveredChannelWasActive()
        {
            Assert.IsTrue(_policy.ShouldFallBackToGeneralAfterUndiscover(undiscoveredChatId: 7, currentActiveChatId: 7));
            Assert.IsFalse(_policy.ShouldFallBackToGeneralAfterUndiscover(undiscoveredChatId: 7, currentActiveChatId: General));
        }

        [Test]
        public void WindowName_OverridePresent_Wins_EvenWhenEmpty()
        {
            Assert.AreEqual("Secret", _policy.ResolveWindowName(7, hasOverrideName: true, overrideName: "Secret", knownEnumName: "General"));
            // A present-but-empty override still wins over the enum name (mirrors the dictionary-key semantics).
            Assert.AreEqual("", _policy.ResolveWindowName(7, hasOverrideName: true, overrideName: "", knownEnumName: "General"));
        }

        [Test]
        public void WindowName_NoOverride_UsesKnownEnumName()
        {
            Assert.AreEqual("General", _policy.ResolveWindowName(0, hasOverrideName: false, overrideName: null, knownEnumName: "General"));
        }

        [Test]
        public void WindowName_NoOverride_UnknownId_UsesGenericFallback()
        {
            Assert.AreEqual("Chat 515100", _policy.ResolveWindowName(515100, hasOverrideName: false, overrideName: null, knownEnumName: null));
        }
    }
}
