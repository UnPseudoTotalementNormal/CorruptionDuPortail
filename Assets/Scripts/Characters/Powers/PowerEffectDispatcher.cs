using System;
using Board;
using ChatSystem;
using CorruptionDuPortail.Domain;
using GameLogic;
using RoleTarget;
using Unity.Collections;
using Unity.Netcode;

namespace Characters.Powers
{
    /// <summary>
    /// Adapter-side dispatch of the Domain <see cref="EffectDescriptor"/> bricks a
    /// <see cref="PowerResolver"/> produces (Wave 3). Records the brick on the observation seam
    /// (so the Story 4.0 golden keeps observing the same ordered trace) then executes the
    /// verbatim singleton/RPC effect. SYNCHRONOUS — no yield between bricks (game-feel).
    ///
    /// The exhaustive <c>switch</c> guards completeness at runtime via the <c>default</c> throw:
    /// a brick variant not yet wired for the powers migrated so far surfaces immediately instead
    /// of silently no-op'ing. Per-power state-store / chat-discover bricks (which need power-local
    /// fields or GetSafeRpcTarget) are added by their own stories (4.2–4.4).
    /// </summary>
    public static class PowerEffectDispatcher
    {
        /// <param name="effect">The brick to record + execute.</param>
        /// <param name="powerLocal">
        /// Optional handler for power-LOCAL bricks (state-store / NetworkList writes that touch a
        /// concrete power's private fields, e.g. <see cref="RegisterInkTarget"/>). Shared bricks are
        /// executed here; anything else is routed to <paramref name="powerLocal"/>, or throws if none.
        /// </param>
        public static void Dispatch(EffectDescriptor effect, Action<EffectDescriptor> powerLocal = null)
        {
            PowerEffectTrace.Record(effect);

            switch (effect)
            {
                case NewTargeting e:
                    RoleTargetSystem.instance.NewTargeting((ulong)e.OwnerSlot, (ulong)e.TargetSlot);
                    break;

                case DiscoverChat e:
                    ChatManager.instance.DiscoverChatRpc(
                        e.ChatId, new FixedString64Bytes(e.ChatName), ResolveTarget(e.Audience));
                    break;

                case CorruptPlayer e:
                    // Verbatim-equivalent to the inline calls: the picked target was corrupted via
                    // its own ref and the owner via GetCharacter(owner). We fetch with
                    // _triggerUpdate:false; the owner site's incidental triggerUpdate:true is dropped
                    // as redundant — OnUsedServer's AskForUpdateAllCharactersRpc fires in the same turn.
                    CharacterManager.instance.GetCharacter((ulong)e.Slot, false).CorruptPlayerServerRpc();
                    break;

                case RevealInfo e:
                    if (e.Broadcast)
                    {
                        CompositionRoot.For(NetworkManager.Singleton).GameInfoRevealer.SendRevealLevelRpc(
                            (ulong)e.TargetSlot, RevealFieldName(e.Field), ToRevealLevel(e.Level), (ulong)e.ViewerSlot, true);
                    }
                    else
                    {
                        CompositionRoot.For(NetworkManager.Singleton).GameInfoRevealer.SetRevealLevel(
                            (ulong)e.TargetSlot, RevealFieldName(e.Field), ToRevealLevel(e.Level), (ulong)e.ViewerSlot);
                    }
                    break;

                case AddCardEffect e:
                    // The original passed the bool as the `object _effectData` argument.
                    CardEffectManager.instance.AddCardEffect((CardEffectID)e.CardEffectId, (ulong)e.Slot, e.Flag);
                    break;

                case ChatLocal e:
                    ChatManager.instance.AddMessageLocal(e.Message, GameValues.CHAT_SERVER_CLIENT_ID, e.WindowId);
                    break;

                case RequestCharacterRefresh:
                    CharacterManager.instance.AskForUpdateAllCharactersRpc();
                    break;

                default:
                    if (powerLocal != null)
                    {
                        powerLocal(effect);
                        return;
                    }
                    throw new NotSupportedException(
                        $"PowerEffectDispatcher: no dispatch wired for brick '{effect}'. " +
                        "Per-power bricks are added by their migration story (Epic 4.x).");
            }
        }

        // Audience -> RPC target. Specific(slot) maps slot -> clientId -> GetSafeRpcTarget; the
        // clientId >= 100 bot interception lives HERE only (NFR5), never in the Domain descriptor.
        private static Unity.Netcode.RpcParams ResolveTarget(PowerEffectAudience audience) => audience.Kind switch
        {
            PowerEffectAudienceKind.Specific => CharacterManager.instance.GetSafeRpcTarget((ulong)audience.LogicalSlot),
            _ => throw new NotSupportedException($"PowerEffectDispatcher: audience {audience} not yet mapped.")
        };

        private static string RevealFieldName(RevealField field) => field switch
        {
            RevealField.CorruptRevealed => nameof(CharacterInfoReveal.isCorruptRevealed),
            RevealField.RoleRevealed    => nameof(CharacterInfoReveal.isRoleRevealed),
            _ => throw new NotSupportedException($"Unknown RevealField {field}")
        };

        private static RevealLevel ToRevealLevel(RevealVisibility visibility) => visibility switch
        {
            RevealVisibility.False    => RevealLevel.False,
            RevealVisibility.Personal => RevealLevel.Personal,
            RevealVisibility.Public   => RevealLevel.Public,
            _ => throw new NotSupportedException($"Unknown RevealVisibility {visibility}")
        };
    }
}
