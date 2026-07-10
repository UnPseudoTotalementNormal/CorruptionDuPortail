using Unity.Netcode;
using CorruptionDuPortail.Domain.Powers;

namespace Characters.Powers.Runtime
{
    /// <summary>
    /// The execution-side context handed to effect executors: the NetworkManager to resolve
    /// per-instance services (via CompositionRoot), and the CURRENT power's state resolver so that
    /// state-write effects (StoreHackTarget, DiscoveredAdd, SetChain…) reach that power's own carrier.
    /// </summary>
    public sealed class EffectRuntime
    {
        public NetworkManager NetworkManager { get; }
        /// <summary>Resolver for the current power's own replicated-state ports (null for stateless powers).</summary>
        public IPowerStateResolver PowerState { get; }

        public EffectRuntime(NetworkManager networkManager, IPowerStateResolver powerState = null)
        {
            NetworkManager = networkManager;
            PowerState = powerState;
        }
    }
}
