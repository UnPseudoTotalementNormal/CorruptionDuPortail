using CorruptionDuPortail.Domain.Powers.State;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PBoundByInk — active (click path): target the picked character, discover its private "Lié par l'encre"
    /// chat (the chat id is power-local state), and register it as an ink target.
    /// </summary>
    public sealed class BoundByInkDecision : IPowerDecision
    {
        public PowerId Id => PowerId.BoundByInk;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            int chatId = ctx.State<IInkChatState>()?.ChatId ?? -1;
            return PowerOutcome.Accept(
                new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot),
                new DiscoverChat(chatId, "Lié par l'encre", PowerEffectAudience.Specific(ctx.TargetSlot)),
                new RegisterInkTarget(ctx.TargetSlot));
        }
    }
}
