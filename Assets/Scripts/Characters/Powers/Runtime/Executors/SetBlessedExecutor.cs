using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class SetBlessedExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(SetBlessed);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (SetBlessed)effect;
            CompositionRoot.For(runtime.NetworkManager).CharacterManager
                .GetCharacter((ulong)e.Slot, false).isBlessed.Value = true;
        }
    }
}
