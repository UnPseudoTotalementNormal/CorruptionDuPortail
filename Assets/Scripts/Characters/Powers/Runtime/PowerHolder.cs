using Unity.Netcode;
using UnityEngine;
using CorruptionDuPortail.Domain.Powers;

namespace Characters.Powers.Runtime
{
    /// <summary>
    /// Generic humble-object host for a POCO power. Holds the UNIFORM replicated state (owner, uses);
    /// the per-power logic is a plain <see cref="IPowerDecision"/> assigned via [SerializeReference] on
    /// the prefab. Stateful powers add a sibling state-carrier NetworkBehaviour the decision reads
    /// through a narrow port. This shell decides NOTHING — it snapshots, calls Decide, writes the
    /// consumed uses, and dispatches the effects. Server-authoritative.
    /// </summary>
    public class PowerHolder : NetworkBehaviour
    {
        public NetworkVariable<ulong> ownerClientId = new();
        public NetworkVariable<int> powerUseLeft = new();

        [SerializeReference] public IPowerDecision decision;

        /// <summary>Run the power's decision (server only) and dispatch its effects.</summary>
        public void ServerRun(in PowerContext context, EffectDispatcher dispatcher, EffectRuntime runtime)
        {
            if (!IsServer)
            {
                return;
            }

            var outcome = decision.Decide(context);
            if (!outcome.Accepted)
            {
                return;
            }

            powerUseLeft.Value -= outcome.UsesConsumed;
            dispatcher.Dispatch(outcome.Effects, runtime);
        }
    }
}
