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

        /// <summary>
        /// NET-09 (epic-network-sync-hardening): when a decision runs on the SERVER on behalf of a specific player,
        /// the slot of the player who must receive its viewer-local effects (owner-local reveal, local chat line,
        /// local card effect). Null = the effects are local to the peer running the decision.
        /// </summary>
        public ulong? LocalViewer { get; }

        /// <summary>NET-09: routes viewer-local effects that have no networked form of their own (card effects).</summary>
        public IViewerEffectRelay ViewerRelay { get; }

        public EffectRuntime(NetworkManager networkManager, IPowerStateResolver powerState = null,
            ulong? localViewer = null, IViewerEffectRelay viewerRelay = null)
        {
            NetworkManager = networkManager;
            PowerState = powerState;
            LocalViewer = localViewer;
            ViewerRelay = viewerRelay;
        }

        /// <summary>True when viewer-local effects must be sent to <see cref="LocalViewer"/> instead of applied here.</summary>
        public bool RoutesToViewer => LocalViewer.HasValue && NetworkManager != null && NetworkManager.IsServer;
    }

    /// <summary>NET-09: server → one viewer delivery of a viewer-local effect.</summary>
    public interface IViewerEffectRelay
    {
        void AddCardEffectForViewer(ulong _viewer, int _cardEffectId, ulong _targetSlot, bool _flag);
    }
}
