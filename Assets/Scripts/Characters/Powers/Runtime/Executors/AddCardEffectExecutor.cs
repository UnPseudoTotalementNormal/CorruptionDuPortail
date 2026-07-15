using System;
using Board;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class AddCardEffectExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(AddCardEffect);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (AddCardEffect)effect;
            CardEffectManager.instance.AddCardEffect((CardEffectID)e.CardEffectId, (ulong)e.Slot, e.Flag);
        }
    }
}
