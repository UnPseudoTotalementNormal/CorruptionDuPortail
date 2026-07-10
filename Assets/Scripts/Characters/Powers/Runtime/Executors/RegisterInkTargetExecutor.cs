using System;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers.State;

namespace Characters.Powers.Runtime.Executors
{
    /// <summary>Realises a <see cref="RegisterInkTarget"/> via PBoundByInk's own carrier (IInkTargetRegister).</summary>
    public sealed class RegisterInkTargetExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(RegisterInkTarget);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (RegisterInkTarget)effect;
            runtime.PowerState.Resolve<IInkTargetRegister>().RegisterInkTarget(e.TargetSlot);
        }
    }
}
