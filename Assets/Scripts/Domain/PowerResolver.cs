using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// Pure, decision-only power-effect resolution (Wave 3, FR11). Each method maps a power's
    /// inputs to the ORDERED <see cref="EffectDescriptor"/> list the adapter then dispatches.
    /// No NGO / FMOD / singleton / clientId — engine-specific ids (card id, chat window) are
    /// passed in as plain ints by the adapter, so the Domain holds no magic constant tied to a
    /// Game enum. Decision-only (NFR4): the list is data; the adapter applies it.
    /// </summary>
    public sealed class PowerResolver
    {
        /// <summary>
        /// PCursedVision (Story 4.1): target is targeted, corrupted, and its corruption revealed
        /// to the owner; a card effect + a server chat line announce whether the target is a
        /// "chosen" (élu); finally the owner corrupts itself and reveals its own corruption.
        /// The verdict text and the card flag branch on <paramref name="targetIsChosen"/>.
        /// </summary>
        /// <param name="ownerSlot">Logical slot of the power owner.</param>
        /// <param name="targetSlot">Logical slot of the picked target.</param>
        /// <param name="targetIsChosen">Whether the target's faction is "chosen" (élu).</param>
        /// <param name="targetPseudo">Display pseudo of the target (composed into the chat line).</param>
        /// <param name="cursedVisionCardEffectId">Adapter-supplied id of the CursedVision card effect.</param>
        /// <param name="serverChatWindowId">Adapter-supplied id of the server chat window.</param>
        public IReadOnlyList<EffectDescriptor> ResolveCursedVision(
            int ownerSlot,
            int targetSlot,
            bool targetIsChosen,
            string targetPseudo,
            int cursedVisionCardEffectId,
            int serverChatWindowId)
        {
            // Chosen: card NOT hidden (flag false) + "est un élu." — else hidden (flag true) + "n'est pas un élu."
            bool cardHidden = !targetIsChosen;
            string verdict = targetIsChosen
                ? $"{targetPseudo} est un élu."
                : $"{targetPseudo} n'est pas un élu.";

            return new EffectDescriptor[]
            {
                new NewTargeting(ownerSlot, targetSlot),
                new CorruptPlayer(targetSlot),
                new RevealInfo(targetSlot, RevealField.CorruptRevealed, RevealVisibility.Personal, ownerSlot, false),
                new AddCardEffect(cursedVisionCardEffectId, targetSlot, cardHidden),
                new ChatLocal(verdict, serverChatWindowId),
                new CorruptPlayer(ownerSlot),
                new RevealInfo(ownerSlot, RevealField.CorruptRevealed, RevealVisibility.Personal, ownerSlot, false),
            };
        }

        /// <summary>
        /// PBoundByInk (Story 4.2), click path: target the picked character, reveal its private
        /// "Lié par l'encre" chat to it, and register it as an ink target. The dedupe guard
        /// (already-targeted) is a precondition the adapter checks before resolving. The chat id
        /// is the power's current chat id (passed in; the adapter owns the NetworkVariable).
        /// </summary>
        public IReadOnlyList<EffectDescriptor> ResolveBoundByInkClick(int ownerSlot, int targetSlot, int chatId)
        {
            return new EffectDescriptor[]
            {
                new NewTargeting(ownerSlot, targetSlot),
                new DiscoverChat(chatId, "Lié par l'encre", PowerEffectAudience.Specific(targetSlot)),
                new RegisterInkTarget(targetSlot),
            };
        }

        /// <summary>
        /// PCorruptingMark (Story 4.3), server click body: target the picked character, store it as
        /// the last-corrupted, raise the corruption-succeeded event, and corrupt it. Validity is an
        /// adapter precondition (the invalid branch raises corruption-failed before resolving).
        /// </summary>
        public IReadOnlyList<EffectDescriptor> ResolveCorruptingMarkClick(int ownerSlot, int targetSlot)
        {
            return new EffectDescriptor[]
            {
                new NewTargeting(ownerSlot, targetSlot),
                new StoreLastCorrupted(targetSlot),
                new CorruptionSucceeded(targetSlot),
                new CorruptPlayer(targetSlot),
            };
        }

        /// <summary>
        /// POmniscience (Story 4.4) — the hack. Server click body: target the picked character,
        /// store it as the hacked target, and reveal its role to the owner. The
        /// <see cref="RevealInfo"/> with <c>Broadcast:true</c> IS the notify-to-target intention
        /// (what the hack tells the target's client). The hardest power is migrated LAST with the
        /// vocabulary already proven on three powers.
        /// </summary>
        public IReadOnlyList<EffectDescriptor> ResolveOmniscienceClick(int ownerSlot, int targetSlot)
        {
            return new EffectDescriptor[]
            {
                new NewTargeting(ownerSlot, targetSlot),
                new StoreHackTarget(targetSlot),
                new RevealInfo(targetSlot, RevealField.RoleRevealed, RevealVisibility.Personal, ownerSlot, true),
                RequestCharacterRefresh.Instance,
            };
        }
    }
}
