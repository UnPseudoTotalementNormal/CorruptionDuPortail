using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class NewTargetingExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(NewTargeting);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (NewTargeting)effect;
            CompositionRoot.For(runtime.NetworkManager).RoleTargetSystem
                .NewTargeting((ulong)e.OwnerSlot, (ulong)e.TargetSlot);
        }
    }
}
