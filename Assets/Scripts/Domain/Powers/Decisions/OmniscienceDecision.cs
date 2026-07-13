using System.Collections.Generic;
using Characters;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// POmniscience (the hack) — active: target the picked character and reveal its role to the owner
    /// (broadcast = the notify-to-target intention). The actual HACK (store the target + mark it "hacked"
    /// for the owner-only glitch, see CardHackGlitch) only applies when the target is an élu (chosen
    /// faction); on an anomaly/marginal the power just reveals the role, no piracy. Ends with a refresh.
    /// </summary>
    public sealed class OmniscienceDecision : IPowerDecision
    {
        public PowerId Id => PowerId.Omniscience;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            bool isChosen = ctx.Roster != null && ctx.Roster.FactionOf(ctx.TargetSlot) == FactionType.chosen;

            var effects = new List<EffectDescriptor> { new NewTargeting(ctx.OwnerSlot, ctx.TargetSlot) };
            if (isChosen)
            {
                effects.Add(new StoreHackTarget(ctx.TargetSlot));
            }
            effects.Add(new RevealInfo(ctx.TargetSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true));
            if (isChosen)
            {
                effects.Add(new RevealInfo(ctx.TargetSlot, RevealField.Hacked, RevealVisibility.Personal, ctx.OwnerSlot, true));
            }
            effects.Add(RequestCharacterRefresh.Instance);

            return PowerOutcome.Accept(effects);
        }
    }
}
