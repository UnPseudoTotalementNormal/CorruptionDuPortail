using System;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers.State;

namespace Characters.Powers.Runtime.Executors
{
    /// <summary>
    /// Realises a <see cref="GrantStolenPowers"/>: routes to PMarqueHurluberluges' own carrier
    /// (<see cref="IStolenPowerGrant"/>), which walks the live roster, filters + randomly picks the stealable
    /// chosen powers and gives one-shot copies to Ugues. Power-local — the engine Power refs and the roster
    /// walk live on the carrier, never in the Domain (same shape as <see cref="GrantLegacyPowerExecutor"/>).
    /// </summary>
    public sealed class GrantStolenPowersExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(GrantStolenPowers);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (GrantStolenPowers)effect;
            runtime.PowerState.Resolve<IStolenPowerGrant>().GrantStolen(e.OwnerSlot);
        }
    }
}
