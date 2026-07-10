using System;
using System.Collections.Generic;
using System.Linq;

namespace CorruptionDuPortail.Domain
{
    // =====================================================================================
    // Story 4.0 — the closed, discriminated power-effect vocabulary (Wave 3, FR11).
    //
    // A power resolves to an ORDERED IReadOnlyList<EffectDescriptor> of INTENTIONS; the
    // adapter dispatches each via an exhaustive type-switch to the real singleton/RPC. The
    // ORDER is carried by the list, never by if-ordering in the adapter (NFR4).
    //
    // Pure Domain (NFR2/NFR5): NO EventReference / RpcParams / clientId / NetworkObject /
    // RevealLevel-engine-enum. Transport identity is a LOGICAL slot (int); the adapter maps
    // slot -> ulong clientId -> GetSafeRpcTarget (the clientId >= 100 bot interception lives
    // ONLY in the adapter). Engine enums (RevealLevel, CardEffectID, ChatWindowIDs) are
    // mirrored as Domain enums / opaque ids and translated verbatim by the adapter.
    //
    // Built as a ValueObject hierarchy (not C# records) so it compiles without the
    // IsExternalInit polyfill that records' `init` need on Unity's .NET Standard 2.1 profile,
    // and matches the existing Domain style. Immutable + value-equatable; ToString feeds the
    // golden trace.
    // =====================================================================================

    /// <summary>Domain mirror of the RPC target audience — no clientId leaks here.</summary>
    public enum PowerEffectAudienceKind { All, Owner, Specific }

    /// <summary>Who an effect notifies. <see cref="Specific"/> carries a LOGICAL slot, never a clientId.</summary>
    public readonly struct PowerEffectAudience : IEquatable<PowerEffectAudience>
    {
        public PowerEffectAudienceKind Kind { get; }
        /// <summary>Valid only when <see cref="Kind"/> is <see cref="PowerEffectAudienceKind.Specific"/>.</summary>
        public int LogicalSlot { get; }

        private PowerEffectAudience(PowerEffectAudienceKind kind, int logicalSlot)
        {
            Kind = kind;
            LogicalSlot = logicalSlot;
        }

        public static readonly PowerEffectAudience All = new(PowerEffectAudienceKind.All, -1);
        public static readonly PowerEffectAudience Owner = new(PowerEffectAudienceKind.Owner, -1);
        public static PowerEffectAudience Specific(int logicalSlot) => new(PowerEffectAudienceKind.Specific, logicalSlot);

        public bool Equals(PowerEffectAudience other) => Kind == other.Kind && LogicalSlot == other.LogicalSlot;
        public override bool Equals(object obj) => obj is PowerEffectAudience o && Equals(o);
        public override int GetHashCode() => unchecked(((int)Kind * 397) ^ LogicalSlot);
        public override string ToString() => Kind == PowerEffectAudienceKind.Specific ? $"Specific({LogicalSlot})" : Kind.ToString();
    }

    /// <summary>Domain mirror of GameInfoRevealer.RevealLevel (False=0 / Personal=10 / Public=20).</summary>
    public enum RevealVisibility { False = 0, Personal = 10, Public = 20 }

    /// <summary>Which CharacterInfoReveal field a <see cref="RevealInfo"/> targets.</summary>
    public enum RevealField { CorruptRevealed, RoleRevealed, ForceCorruptOnRoleRevealed }

    /// <summary>
    /// Base of the closed power-effect union. Sealed variants below; a switch over the base
    /// that misses a variant is a maintenance defect the per-power adapters guard with a
    /// default-throw. Immutable, value-equatable (equality = exact type + ordered components).
    /// </summary>
    public abstract class EffectDescriptor : IEquatable<EffectDescriptor>
    {
        /// <summary>The payload values that define value-equality (order matters). Empty for singletons.</summary>
        protected abstract IEnumerable<object> EqualityComponents { get; }

        public bool Equals(EffectDescriptor other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null || other.GetType() != GetType()) return false;
            return EqualityComponents.SequenceEqual(other.EqualityComponents);
        }

        public override bool Equals(object obj) => Equals(obj as EffectDescriptor);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = GetType().GetHashCode();
                foreach (var component in EqualityComponents)
                {
                    hash = (hash * 397) ^ (component?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }

        public static bool operator ==(EffectDescriptor a, EffectDescriptor b) =>
            a is null ? b is null : a.Equals(b);
        public static bool operator !=(EffectDescriptor a, EffectDescriptor b) => !(a == b);

        public override string ToString()
        {
            var components = EqualityComponents.Select(c => c?.ToString() ?? "null").ToArray();
            return components.Length == 0 ? GetType().Name : $"{GetType().Name}({string.Join(", ", components)})";
        }

        private static readonly object[] None = Array.Empty<object>();
        /// <summary>Helper for parameterless (singleton) variants.</summary>
        protected static IEnumerable<object> NoComponents => None;
    }

    // ---- Common invocation plumbing (pinned as ADAPTER effects) -------------------------
    public sealed class RequestCharacterRefresh : EffectDescriptor
    {
        public static readonly RequestCharacterRefresh Instance = new();
        protected override IEnumerable<object> EqualityComponents => NoComponents;
    }

    // ---- Targeting (RoleTargetSystem) ---------------------------------------------------
    public sealed class NewTargeting : EffectDescriptor
    {
        public int OwnerSlot { get; }
        public int TargetSlot { get; }
        public NewTargeting(int ownerSlot, int targetSlot) { OwnerSlot = ownerSlot; TargetSlot = targetSlot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return OwnerSlot; yield return TargetSlot; } }
    }

    // ---- Reveal (GameInfoRevealer) ------------------------------------------------------
    public sealed class RevealInfo : EffectDescriptor
    {
        public int TargetSlot { get; }
        public RevealField Field { get; }
        public RevealVisibility Level { get; }
        public int ViewerSlot { get; }
        /// <summary>true = SendRevealLevelRpc (broadcast), false = SetRevealLevel (server-local).</summary>
        public bool Broadcast { get; }
        public RevealInfo(int targetSlot, RevealField field, RevealVisibility level, int viewerSlot, bool broadcast)
        {
            TargetSlot = targetSlot; Field = field; Level = level; ViewerSlot = viewerSlot; Broadcast = broadcast;
        }
        protected override IEnumerable<object> EqualityComponents
        {
            get { yield return TargetSlot; yield return Field; yield return Level; yield return ViewerSlot; yield return Broadcast; }
        }
    }

    // ---- Corruption (Character.CorruptPlayer + ICorrupterPower events) -------------------
    public sealed class CorruptPlayer : EffectDescriptor
    {
        public int Slot { get; }
        public CorruptPlayer(int slot) { Slot = slot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return Slot; } }
    }

    public sealed class CorruptionSucceeded : EffectDescriptor
    {
        public int Slot { get; }
        public CorruptionSucceeded(int slot) { Slot = slot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return Slot; } }
    }

    public sealed class CorruptionFailed : EffectDescriptor
    {
        public int Slot { get; }
        public CorruptionFailed(int slot) { Slot = slot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return Slot; } }
    }

    // ---- Card effects (CardEffectManager) -----------------------------------------------
    public sealed class AddCardEffect : EffectDescriptor
    {
        /// <summary>Opaque mirror of CardEffectID (the adapter casts back).</summary>
        public int CardEffectId { get; }
        public int Slot { get; }
        public bool Flag { get; }
        public AddCardEffect(int cardEffectId, int slot, bool flag) { CardEffectId = cardEffectId; Slot = slot; Flag = flag; }
        protected override IEnumerable<object> EqualityComponents { get { yield return CardEffectId; yield return Slot; yield return Flag; } }
    }

    // ---- Chat (ChatManager) -------------------------------------------------------------
    public sealed class ChatLocal : EffectDescriptor
    {
        public string Message { get; }
        public int WindowId { get; }
        public ChatLocal(string message, int windowId) { Message = message; WindowId = windowId; }
        protected override IEnumerable<object> EqualityComponents { get { yield return Message; yield return WindowId; } }
    }

    public sealed class DiscoverChat : EffectDescriptor
    {
        public int ChatId { get; }
        public string ChatName { get; }
        public PowerEffectAudience Audience { get; }
        public DiscoverChat(int chatId, string chatName, PowerEffectAudience audience) { ChatId = chatId; ChatName = chatName; Audience = audience; }
        protected override IEnumerable<object> EqualityComponents { get { yield return ChatId; yield return ChatName; yield return Audience; } }
    }

    // ---- State stores (NetworkVariable / NetworkList writes, pinned as adapter effects) --
    public sealed class StoreHackTarget : EffectDescriptor
    {
        public int TargetSlot { get; }
        public StoreHackTarget(int targetSlot) { TargetSlot = targetSlot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return TargetSlot; } }
    }

    public sealed class StoreLastCorrupted : EffectDescriptor
    {
        public int TargetSlot { get; }
        public StoreLastCorrupted(int targetSlot) { TargetSlot = targetSlot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return TargetSlot; } }
    }

    public sealed class RegisterInkTarget : EffectDescriptor
    {
        public int TargetSlot { get; }
        public RegisterInkTarget(int targetSlot) { TargetSlot = targetSlot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return TargetSlot; } }
    }

    // ====================================================================================
    // Active-power effect vocabulary (ported from the v1 branch, proven). Each maps to one
    // typed IEffectExecutor in the runtime registry. Chat sender ids are ulong (the sentinel
    // is ulong.MaxValue).
    // ====================================================================================

    public sealed class AddToChain : EffectDescriptor
    {
        public int Slot { get; }
        public AddToChain(int slot) { Slot = slot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return Slot; } }
    }

    public sealed class HealPlayer : EffectDescriptor
    {
        public int Slot { get; }
        public HealPlayer(int slot) { Slot = slot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return Slot; } }
    }

    public sealed class SetBlessed : EffectDescriptor
    {
        public int Slot { get; }
        public SetBlessed(int slot) { Slot = slot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return Slot; } }
    }

    public sealed class SetEliminated : EffectDescriptor
    {
        public int Slot { get; }
        public SetEliminated(int slot) { Slot = slot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return Slot; } }
    }

    /// <summary>Public reveal via SetRevealLevelRpc (no observer, Public level).</summary>
    public sealed class RevealPublic : EffectDescriptor
    {
        public int TargetSlot { get; }
        public RevealField Field { get; }
        public RevealPublic(int targetSlot, RevealField field) { TargetSlot = targetSlot; Field = field; }
        protected override IEnumerable<object> EqualityComponents { get { yield return TargetSlot; yield return Field; } }
    }

    /// <summary>Server-broadcast chat (ChatManager.ReceiveChatMessageRpc). Audience.All → no rpcParams; Specific → GetSafeRpcTarget.</summary>
    public sealed class ChatBroadcast : EffectDescriptor
    {
        public string Message { get; }
        public int WindowId { get; }
        public PowerEffectAudience Audience { get; }
        public ChatBroadcast(string message, int windowId, PowerEffectAudience audience)
        {
            Message = message; WindowId = windowId; Audience = audience;
        }
        protected override IEnumerable<object> EqualityComponents
        {
            get { yield return Message; yield return WindowId; yield return Audience; }
        }
    }

    /// <summary>Server-routed chat (ChatManager.SendChatMessageServerRpc). Sender is the server sentinel (executor-supplied).</summary>
    public sealed class ChatSendServer : EffectDescriptor
    {
        public string Message { get; }
        public int WindowId { get; }
        public ChatSendServer(string message, int windowId)
        {
            Message = message; WindowId = windowId;
        }
        protected override IEnumerable<object> EqualityComponents
        {
            get { yield return Message; yield return WindowId; }
        }
    }

    /// <summary>Server write of a character's messageLeft NetworkVariable (PInfiniteMessage).</summary>
    public sealed class SetMessageLeft : EffectDescriptor
    {
        public int Slot { get; }
        public int Value { get; }
        public SetMessageLeft(int slot, int value) { Slot = slot; Value = value; }
        protected override IEnumerable<object> EqualityComponents { get { yield return Slot; yield return Value; } }
    }

    // ---- Give-power (power-local: the engine Power ref lives on the power's carrier) -------
    /// <summary>PLegacy: grant the power's configured legacy power to the owner (power-local carrier).</summary>
    public sealed class GrantLegacyPower : EffectDescriptor
    {
        public int OwnerSlot { get; }
        public GrantLegacyPower(int ownerSlot) { OwnerSlot = ownerSlot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return OwnerSlot; } }
    }

    /// <summary>PReincarnation: grant every power of the from-role's character to the owner.</summary>
    public sealed class GrantRolePowers : EffectDescriptor
    {
        public int OwnerSlot { get; }
        public int FromRoleSlot { get; }
        public GrantRolePowers(int ownerSlot, int fromRoleSlot) { OwnerSlot = ownerSlot; FromRoleSlot = fromRoleSlot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return OwnerSlot; yield return FromRoleSlot; } }
    }

    /// <summary>
    /// PMarqueHurluberluges (Ugues): grant the owner his stolen one-shot power copies. Which powers are
    /// stolen (the chosen-faction / non-passive filter + random-distinct pick over the live roster) is
    /// engine-coupled and stays power-local on the carrier, exactly like <see cref="GrantLegacyPower"/> —
    /// the pure decision only emits the "grant Ugues his stolen copies" intention.
    /// </summary>
    public sealed class GrantStolenPowers : EffectDescriptor
    {
        public int OwnerSlot { get; }
        public GrantStolenPowers(int ownerSlot) { OwnerSlot = ownerSlot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return OwnerSlot; } }
    }

    /// <summary>PReincarnation: broadcast a change to the power's isPassive flag (power-local).</summary>
    public sealed class SetPassiveBroadcast : EffectDescriptor
    {
        public bool Value { get; }
        public SetPassiveBroadcast(bool value) { Value = value; }
        protected override IEnumerable<object> EqualityComponents { get { yield return Value; } }
    }

    /// <summary>PCardsShuffling: add a slot to the power's discovered-list (power-local state write).</summary>
    public sealed class DiscoveredAdd : EffectDescriptor
    {
        public int Slot { get; }
        public DiscoveredAdd(int slot) { Slot = slot; }
        protected override IEnumerable<object> EqualityComponents { get { yield return Slot; } }
    }
}
