using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class AddToChainExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(AddToChain);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (AddToChain)effect;
            CompositionRoot.For(runtime.NetworkManager).ChainingManager
                .AddCharacterToChainingList((ulong)e.Slot);
        }
    }
}
