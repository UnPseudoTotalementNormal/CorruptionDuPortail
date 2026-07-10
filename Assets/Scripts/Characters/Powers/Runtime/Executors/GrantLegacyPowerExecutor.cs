using System;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers.State;

namespace Characters.Powers.Runtime.Executors
{
    /// <summary>
    /// Realises a <see cref="GrantLegacyPower"/>: routes to PLegacy's own carrier (ILegacyGrant), which
    /// marks the legacy inherited and gives its configured legacy power to the owner. Power-local — the
    /// engine Power ref lives on the carrier, never in the Domain.
    /// </summary>
    public sealed class GrantLegacyPowerExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(GrantLegacyPower);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (GrantLegacyPower)effect;
            runtime.PowerState.Resolve<ILegacyGrant>().GrantLegacy(e.OwnerSlot);
        }
    }
}
