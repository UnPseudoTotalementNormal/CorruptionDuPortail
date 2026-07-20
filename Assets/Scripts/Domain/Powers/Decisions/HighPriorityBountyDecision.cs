using System.Collections.Generic;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PHighPriorityBounty — active: NewTargeting(owner, owner) (self-target) is unconditional; if the target
    /// is the Robot, eliminate it, broadcast the elimination to everyone, and publicly reveal its role;
    /// otherwise chain the OWNER and warn the owner privately. RequestCharacterRefresh tails both branches.
    /// </summary>
    public sealed class HighPriorityBountyDecision : IPowerDecision
    {
        public PowerId Id => PowerId.HighPriorityBounty;
        public bool IsPassive => false;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor> { new NewTargeting(ctx.OwnerSlot, ctx.OwnerSlot) };
            bool hitTheRobot = ctx.Roster.IsRobot(ctx.TargetSlot);
            if (hitTheRobot)
            {
                effects.Add(new SetEliminated(ctx.TargetSlot));
                effects.Add(new ChatBroadcast(
                    $"{ctx.Roster.PseudoOf(ctx.TargetSlot)} était le robot et a été éliminé par {ctx.Roster.RoleNameOf(ctx.OwnerSlot)}.",
                    ChatWindows.Server, PowerEffectAudience.All));
                effects.Add(new RevealPublic(ctx.TargetSlot, RevealField.RoleRevealed));
            }
            else
            {
                effects.Add(new AddToChain(ctx.OwnerSlot));
                effects.Add(new ChatBroadcast(
                    "Votre cible n'était pas le robot. Vous serez enchaîné à la fin de l'éveil.",
                    ChatWindows.Server, PowerEffectAudience.Specific(ctx.OwnerSlot)));
            }
            effects.Add(RequestCharacterRefresh.Instance);
            return PowerOutcome.Accept(effects, hitTheRobot ? PowerVerdict.Correct : PowerVerdict.Incorrect);
        }
    }
}
