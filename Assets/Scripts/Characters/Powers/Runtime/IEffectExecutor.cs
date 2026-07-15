using System;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime
{
    /// <summary>
    /// Executes ONE effect-descriptor type against the live engine (singletons / RPC). One executor
    /// per effect verb, registered in the <see cref="EffectDispatcher"/> by <see cref="DescriptorType"/>.
    /// Adding a new effect = one new descriptor (Domain data) + one new executor here — no shared file
    /// edited, no central switch.
    /// </summary>
    public interface IEffectExecutor
    {
        Type DescriptorType { get; }
        void Execute(EffectDescriptor effect, EffectRuntime runtime);
    }
}
