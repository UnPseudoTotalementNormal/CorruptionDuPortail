using System;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers.State;

namespace Characters.Powers.Runtime.Executors
{
    /// <summary>Realises a <see cref="DiscoveredAdd"/> via PCardsShuffling's own carrier (IDiscoveredAdd).</summary>
    public sealed class DiscoveredAddExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(DiscoveredAdd);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (DiscoveredAdd)effect;
            runtime.PowerState.Resolve<IDiscoveredAdd>().DiscoveredAdd(e.Slot);
        }
    }
}
