using System.Collections.Generic;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PPersonalBeacons — passive (on spawn): reveal every Robot's forceCorruptOnRoleRevealed to the owner
    /// (broadcast). The beacon-object spawn + the corrupted-beacon local chat are power-local plumbing kept
    /// on the carrier; this decision covers the robot reveal (which was a latent NRE in the old Awake path).
    /// </summary>
    public sealed class PersonalBeaconsDecision : IPowerDecision
    {
        public PowerId Id => PowerId.PersonalBeacons;
        public bool IsPassive => true;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor>();
            foreach (var slot in ctx.Roster.Slots)
            {
                if (ctx.Roster.IsRobot(slot))
                {
                    effects.Add(new RevealInfo(slot, RevealField.ForceCorruptOnRoleRevealed, RevealVisibility.Personal, ctx.OwnerSlot, true));
                }
            }
            return effects.Count == 0 ? PowerOutcome.AcceptEmpty() : PowerOutcome.Accept(effects);
        }
    }
}
