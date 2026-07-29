using System;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers.State;

namespace Characters.Powers.Runtime.Executors
{
    /// <summary>
    /// Realises a <see cref="GrantExtraUse"/>: routes to PChainedByTheShadows's own carrier (IExtraUseGrant),
    /// which bumps the power's use count for the night and marks the once-per-night bonus consumed. Power-local
    /// — the use-count NetworkVariable lives on the carrier, never in the Domain (mirror of GrantLegacyPower).
    /// </summary>
    public sealed class GrantExtraUseExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(GrantExtraUse);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            runtime.PowerState.Resolve<IExtraUseGrant>().GrantExtraUse();
        }
    }
}
