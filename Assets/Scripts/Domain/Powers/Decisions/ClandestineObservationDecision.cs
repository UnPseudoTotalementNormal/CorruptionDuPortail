using CorruptionDuPortail.Domain.Powers.State;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PClandestineObservation — passive (owner awakening): announce to the owner how many players targeted
    /// the observed role. No characters carry the role → enum label + ": 0." (period); otherwise the role
    /// display name + ": {count}" (no period). The engine reads are supplied by the report port.
    /// </summary>
    public sealed class ClandestineObservationDecision : IPowerDecision
    {
        public PowerId Id => PowerId.ClandestineObservation;
        public bool IsPassive => true;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var report = ctx.State<IClandestineReport>();
            string message = report.HasCharacters
                ? $"Total de personne qui ont ciblé le rôle \"{report.RoleLabel}\": {report.DistinctTargetingCount}"
                : $"Total de personne qui ont ciblé le rôle \"{report.RoleLabel}\": 0.";
            return PowerOutcome.Accept(new ChatBroadcast(message, ChatWindows.Server, PowerEffectAudience.Specific(ctx.OwnerSlot)));
        }
    }
}
