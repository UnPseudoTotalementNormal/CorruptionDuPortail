using CorruptionDuPortail.Domain.Powers;

namespace Characters.Powers.Runtime
{
    /// <summary>
    /// Live <see cref="IPowerStateResolver"/> over a single carrier object (normally the power itself). A
    /// state-carrier power implements its narrow state/event ports (IHackTargetState, ICorruptionEvents, …);
    /// this adapter resolves each port to the carrier when it implements it, else null. The pure decision
    /// never sees a NetworkVariable — only the port. EditMode tests supply a FakeState instead.
    /// </summary>
    public sealed class PowerStateAdapter : IPowerStateResolver
    {
        private readonly object _carrier;

        public PowerStateAdapter(object carrier) => _carrier = carrier;

        public TPort Resolve<TPort>() where TPort : class => _carrier as TPort;
    }
}
