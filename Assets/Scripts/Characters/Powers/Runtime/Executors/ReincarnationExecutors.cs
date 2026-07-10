using System;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers.State;

namespace Characters.Powers.Runtime.Executors
{
    /// <summary>Realises a <see cref="SetPassiveBroadcast"/> via PReincarnation's own carrier (ISetPassiveState).</summary>
    public sealed class SetPassiveBroadcastExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(SetPassiveBroadcast);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (SetPassiveBroadcast)effect;
            runtime.PowerState.Resolve<ISetPassiveState>().SetPassive(e.Value);
        }
    }

    /// <summary>Realises a <see cref="GrantRolePowers"/> via PReincarnation's own carrier (IGrantRolePowers).</summary>
    public sealed class GrantRolePowersExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(GrantRolePowers);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (GrantRolePowers)effect;
            runtime.PowerState.Resolve<IGrantRolePowers>().GrantRolePowers(e.OwnerSlot, e.FromRoleSlot);
        }
    }
}
