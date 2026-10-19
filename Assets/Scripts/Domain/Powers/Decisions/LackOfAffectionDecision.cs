using System.Collections.Generic;
using Characters;
using CorruptionDuPortail.Domain.PlayerIcons;

namespace CorruptionDuPortail.Domain.Powers.Decisions
{
    /// <summary>
    /// PLackOfAffection — runs on the server for the CONTACTED target. If the target is a "chosen" faction, reveal
    /// the SENDER's (owner's) role AND corruption state to the target (server-local); and if this is the true-local
    /// target, post a local chat line: naming the sender's role for a chosen, only the visit otherwise (GD wording:
    /// a non-élu "ne recevra que la visite et non l'identité"). The faction-keyed contact SOUND stays adapter-side.
    /// Every visit also leaves two private marks (GD: "l'orpheline envoie une icône à ceux qu'elle cible"):
    /// the target sees this power's icon on the Orpheline's thumbnail in the role bar, so an anomaly knows she
    /// came; and the Orpheline sees a mark on the target's CARD (the player, never their role thumbnail, which
    /// would reveal the target's role).
    /// </summary>
    public sealed class LackOfAffectionDecision : IPowerDecision
    {
        public PowerId Id => PowerId.LackOfAffection;
        public bool IsPassive => false;

        /// <summary>Opaque icon identity, set by the adapter to the declaring Power's NetworkObjectId.</summary>
        public ulong IconId;

        /// <summary>Opaque mirror of the CardEffectID marking a visited player's card, set by the adapter.</summary>
        public int VisitCardEffectId;

        public PowerOutcome Decide(in PowerContext ctx)
        {
            var effects = new List<EffectDescriptor>();
            bool chosen = ctx.Roster.FactionOf(ctx.TargetSlot) == FactionType.chosen;
            if (chosen)
            {
                effects.Add(new RevealInfo(ctx.OwnerSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ctx.TargetSlot, false));
                effects.Add(new RevealInfo(ctx.OwnerSlot, RevealField.CorruptRevealed, RevealVisibility.Personal, ctx.TargetSlot, false));
            }
            if (ctx.IsTrueLocalTarget)
            {
                string line = chosen ? $"{ctx.Roster.RoleNameOf(ctx.OwnerSlot)} est venu(e) vous voir..." : "Quelqu'un est venu vous voir...";
                effects.Add(new ChatLocal(line, ChatWindows.Server));
            }
            // The target's view: the Orpheline's thumbnail carries her icon until the next night starts.
            effects.Add(new AddPlayerIcon(IconId, ctx.OwnerSlot, ctx.TargetSlot, PlayerIconLifetime.ClearAtAwakeningStart));
            // The Orpheline's view: the visited player's card keeps a mark.
            effects.Add(new AddCardEffect(VisitCardEffectId, ctx.TargetSlot, false, ctx.OwnerSlot));
            return PowerOutcome.Accept(effects);
        }
    }
}
