using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class SetMessageLeftExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(SetMessageLeft);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (SetMessageLeft)effect;
            CompositionRoot.For(runtime.NetworkManager).CharacterManager
                .GetCharacter((ulong)e.Slot, true).messageLeft.Value = e.Value;
        }
    }
}
