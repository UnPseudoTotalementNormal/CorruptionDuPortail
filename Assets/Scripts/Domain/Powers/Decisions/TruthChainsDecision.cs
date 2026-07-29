using System.Collections.Generic;
using Characters;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PTruthChains — active: target the picked character; if it is an "anomaly" faction, add it to the
    /// chaining list and announce (server-routed chat) that it will be chained. NewTargeting is unconditional.
    /// </summary>
    public sealed class TruthChainsDecision : IPowerDecision
    {
        public PowerId Id => PowerId.TruthChains;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor> { new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot) };
            bool caughtAnomaly = ctx.Roster.FactionOf(ctx.TargetSlot) == FactionType.anomaly;
            if (caughtAnomaly)
            {
                effects.Add(new AddToChain(ctx.TargetSlot));
                effects.Add(new ChatSendServer(
                    $"{ctx.Roster.PseudoOf(ctx.TargetSlot)} sera lié par les chaînes de la vérité.", ChatWindows.Server));
            }
            return PowerOutcome.Accept(effects, caughtAnomaly ? PowerVerdict.Correct : PowerVerdict.Incorrect);
        }
    }
}
