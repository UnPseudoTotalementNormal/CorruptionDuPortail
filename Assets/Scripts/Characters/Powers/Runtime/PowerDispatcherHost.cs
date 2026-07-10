using System;
using System.Linq;

namespace Characters.Powers.Runtime
{
    /// <summary>
    /// Boot-time home of the shared <see cref="EffectDispatcher"/>: reflection-discovers every
    /// IEffectExecutor in this assembly and registers it by descriptor type. Built once on first access.
    /// This is the "register the executors at boot" step of the v2 wiring — adding an effect + executor
    /// needs no edit here.
    /// </summary>
    public static class PowerDispatcherHost
    {
        private static readonly EffectDispatcher _dispatcher = new EffectDispatcher(
            typeof(IEffectExecutor).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface
                            && typeof(IEffectExecutor).IsAssignableFrom(t)
                            && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (IEffectExecutor)Activator.CreateInstance(t)));

        public static EffectDispatcher Dispatcher => _dispatcher;
    }
}
