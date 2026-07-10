using System;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers.State;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class CorruptionSucceededExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(CorruptionSucceeded);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
            => runtime.PowerState.Resolve<ICorruptionEvents>().RaiseSucceeded(((CorruptionSucceeded)effect).Slot);
    }

    public sealed class CorruptionFailedExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(CorruptionFailed);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
            => runtime.PowerState.Resolve<ICorruptionEvents>().RaiseFailed(((CorruptionFailed)effect).Slot);
    }

    public sealed class StoreLastCorruptedExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(StoreLastCorrupted);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
            => runtime.PowerState.Resolve<ILastCorruptedState>().StoreLastCorrupted(((StoreLastCorrupted)effect).TargetSlot);
    }
}
