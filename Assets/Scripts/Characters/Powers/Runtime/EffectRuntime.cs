using Unity.Netcode;

namespace Characters.Powers.Runtime
{
    /// <summary>
    /// The execution-side context handed to effect executors: the NetworkManager to resolve
    /// per-instance services (via CompositionRoot) and honour server authority / bot interception.
    /// Kept minimal; grows only if executors need shared resolved services.
    /// </summary>
    public sealed class EffectRuntime
    {
        public NetworkManager NetworkManager { get; }
        public EffectRuntime(NetworkManager networkManager) => NetworkManager = networkManager;
    }
}
