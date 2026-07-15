using System;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers.State;

namespace Characters.Powers.Runtime.Executors
{
    /// <summary>Writes POmniscience's hacked target into the power's own state carrier (power-local).</summary>
    public sealed class StoreHackTargetExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(StoreHackTarget);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (StoreHackTarget)effect;
            runtime.PowerState.Resolve<IHackTargetState>().StoreHackTarget(e.TargetSlot);
        }
    }
}
