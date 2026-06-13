using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure chat channel routing / visibility policy (Story 11.3). Chat visibility IS the deduction
    /// information asymmetry — who reads what is gameplay-sacred — so these rules are extracted to a POCO
    /// and pinned with EditMode tests: a future change that, say, lets the read-only server channel be
    /// typed into, or fails to fall back when the active channel is undiscovered, becomes a red unit test
    /// instead of a silent design break invisible to the compiler. No engine types — the adapter owns the
    /// discovered-id set, the active-channel state, all RPC dispatch, and (NFR5 / AC3) the clientId>=100
    /// bot interception via <c>GetSafeRpcTarget</c>, which is NEVER moved here. Decision-only (NFR4).
    /// </summary>
    public sealed class ChatChannelPolicy
    {
        /// <summary>A message may be displayed only on a channel the recipient has discovered.</summary>
        public bool IsMessageVisible(int chatId, ISet<int> discoveredChatIds)
            => discoveredChatIds.Contains(chatId);

        /// <summary>A channel may be made active only once it has been discovered.</summary>
        public bool CanActivateChannel(int chatId, ISet<int> discoveredChatIds)
            => discoveredChatIds.Contains(chatId);

        /// <summary>
        /// The player may send only non-empty text, and never into the read-only server channel.
        /// </summary>
        public bool CanSendMessage(string text, int activeChatId, int serverChatId)
            => !string.IsNullOrEmpty(text) && activeChatId != serverChatId;

        /// <summary>
        /// When a channel is undiscovered, the active channel falls back to the general channel only if
        /// the undiscovered channel WAS the active one (mirrors the original <c>activeChatId == _chatId</c>
        /// guard exactly — including the redundant re-activation in the degenerate "undiscover the active
        /// general channel" edge, which the adapter still performs).
        /// </summary>
        public bool ShouldFallBackToGeneralAfterUndiscover(int undiscoveredChatId, int currentActiveChatId)
            => currentActiveChatId == undiscoveredChatId;

        /// <summary>
        /// Channel display-name policy: an explicit override wins (whenever one is present, even if empty);
        /// else a known channel id uses its enum name; else a generic "Chat {id}" fallback. The adapter
        /// resolves the engine-side lookups (the override dictionary, the Game-enum reflection) and passes
        /// the plain results — <paramref name="hasOverrideName"/>/<paramref name="overrideName"/> from the
        /// dictionary, <paramref name="knownEnumName"/> non-null iff the id is a defined channel enum value.
        /// </summary>
        public string ResolveWindowName(int chatId, bool hasOverrideName, string overrideName, string knownEnumName)
        {
            if (hasOverrideName) return overrideName;
            if (knownEnumName != null) return knownEnumName;
            return $"Chat {chatId}";
        }
    }
}
