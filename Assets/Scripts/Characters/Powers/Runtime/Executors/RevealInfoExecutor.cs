using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    /// <summary>
    /// Executes a <see cref="RevealInfo"/>: broadcast → GameInfoRevealer.SendRevealLevelRpc, else the
    /// server-local SetRevealLevel. The revealer is resolved per-NetworkManager via CompositionRoot.
    /// </summary>
    public sealed class RevealInfoExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(RevealInfo);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (RevealInfo)effect;
            var revealer = CompositionRoot.For(runtime.NetworkManager).GameInfoRevealer;
            if (e.Broadcast)
            {
                revealer.SendRevealLevelRpc((ulong)e.TargetSlot, FieldName(e.Field), ToLevel(e.Level), (ulong)e.ViewerSlot, true);
            }
            else
            {
                revealer.SetRevealLevel((ulong)e.TargetSlot, FieldName(e.Field), ToLevel(e.Level), (ulong)e.ViewerSlot);
            }
        }

        private static string FieldName(RevealField field) => field switch
        {
            RevealField.CorruptRevealed => nameof(CharacterInfoReveal.isCorruptRevealed),
            RevealField.RoleRevealed => nameof(CharacterInfoReveal.isRoleRevealed),
            RevealField.ForceCorruptOnRoleRevealed => nameof(CharacterInfoReveal.forceCorruptOnRoleRevealed),
            _ => throw new NotSupportedException($"Unknown RevealField {field}")
        };

        private static RevealLevel ToLevel(RevealVisibility visibility) => visibility switch
        {
            RevealVisibility.False => RevealLevel.False,
            RevealVisibility.Personal => RevealLevel.Personal,
            RevealVisibility.Public => RevealLevel.Public,
            _ => throw new NotSupportedException($"Unknown RevealVisibility {visibility}")
        };
    }
}
