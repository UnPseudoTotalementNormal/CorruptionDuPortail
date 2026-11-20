using CorruptionDuPortail.Domain.Localization;
using CorruptionDuPortail.Domain.PlayerIcons;
using CorruptionDuPortail.Domain.Powers.State;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PBoundByInk — active (click path): target the picked character, discover its private "Lié par l'encre"
    /// chat (the chat id is power-local state), and register it as an ink target. The link also leaves two private
    /// marks, like the Orpheline's visit (T9, maquettes placeholders v1 M1 + M2): the Messager sees the phone on the
    /// linked player's CARD, and the linked player sees it on the Messager's thumbnail until the next night.
    /// </summary>
    public sealed class BoundByInkDecision : IPowerDecision
    {
        public PowerId Id => PowerId.BoundByInk;
        public bool IsPassive => false;

        /// <summary>Opaque icon identity, set by the adapter to the declaring Power's NetworkObjectId.</summary>
        public ulong IconId;

        /// <summary>Opaque mirror of the CardEffectID marking the linked player's card, set by the adapter.</summary>
        public int LinkCardEffectId;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            int chatId = ctx.State<IInkChatState>()?.ChatId ?? -1;
            return PowerOutcome.Accept(
                new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot),
                new DiscoverChat(chatId, Loc.Token("Lié par l'encre"), PowerEffectAudience.Specific(ctx.TargetSlot)),
                new RegisterInkTarget(ctx.TargetSlot),
                // The linked player's view: the Messager's thumbnail carries the phone until the next night starts.
                new AddPlayerIcon(IconId, ctx.OwnerSlot, ctx.TargetSlot, PlayerIconLifetime.ClearAtAwakeningStart),
                // The Messager's view: the linked player's card keeps the phone.
                new AddCardEffect(LinkCardEffectId, ctx.TargetSlot, false, ctx.OwnerSlot));
        }
    }
}
