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
            // A decision naming its own viewer (one decision, two boards: LackOfAffection marks the card on the
            // Orpheline's board while its other effects go to the target) is relayed to that player from the server.
            if (e.ViewerSlot.HasValue && runtime.ViewerRelay != null && runtime.NetworkManager != null && runtime.NetworkManager.IsServer)
            {
                runtime.ViewerRelay.AddCardEffectForViewer((ulong)e.ViewerSlot.Value, e.CardEffectId, (ulong)e.Slot, e.Flag);
                return;
            }
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
