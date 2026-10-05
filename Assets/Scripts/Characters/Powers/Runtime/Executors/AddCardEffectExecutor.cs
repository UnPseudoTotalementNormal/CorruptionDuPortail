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
            // NET-09: a card effect produced by a SERVER-run decision for a specific player is shown on THAT player's
            // board (relayed by the power), never on the host's.
            if (runtime.RoutesToViewer && runtime.ViewerRelay != null)
            {
                runtime.ViewerRelay.AddCardEffectForViewer(runtime.LocalViewer.Value, e.CardEffectId, (ulong)e.Slot, e.Flag);
                return;
            }
            CardEffectManager.instance.AddCardEffect((CardEffectID)e.CardEffectId, (ulong)e.Slot, e.Flag);
        }
    }
}
