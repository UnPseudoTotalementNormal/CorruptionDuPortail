using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class HealPlayerExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(HealPlayer);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (HealPlayer)effect;
            CompositionRoot.For(runtime.NetworkManager).CharacterManager
                .GetCharacter((ulong)e.Slot, false).HealPlayerServerRpc();
        }
    }
}
