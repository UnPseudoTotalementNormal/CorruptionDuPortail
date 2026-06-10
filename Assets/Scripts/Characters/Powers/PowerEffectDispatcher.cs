using System;
using Board;
using ChatSystem;
using CorruptionDuPortail.Domain;
using GameLogic;
using RoleTarget;

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
        public static void Dispatch(EffectDescriptor effect)
        {
            PowerEffectTrace.Record(effect);

            switch (effect)
            {
                case NewTargeting e:
                    RoleTargetSystem.instance.NewTargeting((ulong)e.OwnerSlot, (ulong)e.TargetSlot);
                    break;

                case CorruptPlayer e:
                    // Verbatim-equivalent to the inline calls: the picked target was corrupted via
                    // its own ref and the owner via GetCharacter(owner). We fetch with
                    // _triggerUpdate:false; the owner site's incidental triggerUpdate:true is dropped
                    // as redundant — OnUsedServer's AskForUpdateAllCharactersRpc fires in the same turn.
                    GameManager.instance.characterManager.GetCharacter((ulong)e.Slot, false).CorruptPlayerServerRpc();
                    break;

                case RevealInfo e:
                    if (e.Broadcast)
                    {
                        GameManager.instance.gameInfoRevealer.SendRevealLevelRpc(
                            (ulong)e.TargetSlot, RevealFieldName(e.Field), ToRevealLevel(e.Level), (ulong)e.ViewerSlot, true);
                    }
                    else
                    {
                        GameManager.instance.gameInfoRevealer.SetRevealLevel(
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

                default:
                    throw new NotSupportedException(
                        $"PowerEffectDispatcher: no dispatch wired for brick '{effect}'. " +
                        "Per-power bricks are added by their migration story (Epic 4.x).");
            }
        }

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
