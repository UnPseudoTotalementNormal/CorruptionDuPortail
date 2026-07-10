using System;
using GameLogic;
using Unity.Netcode;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime
{
    /// <summary>Shared helpers for effect executors: audience → RpcParams, reveal-field name, reveal level.</summary>
    internal static class EffectExecutorHelpers
    {
        public static RpcParams ResolveTarget(PowerEffectAudience audience, EffectRuntime runtime) =>
            audience.Kind == PowerEffectAudienceKind.Specific
                ? CompositionRoot.For(runtime.NetworkManager).CharacterManager.GetSafeRpcTarget((ulong)audience.LogicalSlot)
                : default;

        public static string RevealFieldName(RevealField field) => field switch
        {
            RevealField.CorruptRevealed => nameof(CharacterInfoReveal.isCorruptRevealed),
            RevealField.RoleRevealed => nameof(CharacterInfoReveal.isRoleRevealed),
            RevealField.ForceCorruptOnRoleRevealed => nameof(CharacterInfoReveal.forceCorruptOnRoleRevealed),
            _ => throw new NotSupportedException($"Unknown RevealField {field}")
        };

        public static RevealLevel ToRevealLevel(RevealVisibility visibility) => visibility switch
        {
            RevealVisibility.False => RevealLevel.False,
            RevealVisibility.Personal => RevealLevel.Personal,
            RevealVisibility.Public => RevealLevel.Public,
            _ => throw new NotSupportedException($"Unknown RevealVisibility {visibility}")
        };
    }
}
