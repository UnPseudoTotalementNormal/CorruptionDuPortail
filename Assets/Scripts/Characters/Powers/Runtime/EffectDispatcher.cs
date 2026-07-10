using System;
using System.Collections.Generic;
using System.Linq;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime
{
    /// <summary>
    /// Routes each <see cref="EffectDescriptor"/> to its registered <see cref="IEffectExecutor"/> by
    /// concrete type. Decentralised dispatch (a dictionary, not a switch): a new effect adds an executor
    /// and self-registers — no edit here. A missing executor throws immediately (a completeness
    /// guard-test asserts every descriptor type has one).
    /// </summary>
    public sealed class EffectDispatcher
    {
        private readonly Dictionary<Type, IEffectExecutor> _byType;

        public EffectDispatcher(IEnumerable<IEffectExecutor> executors)
            => _byType = executors.ToDictionary(e => e.DescriptorType);

        public void Dispatch(IReadOnlyList<EffectDescriptor> effects, EffectRuntime runtime)
        {
            foreach (var effect in effects)
            {
                if (!_byType.TryGetValue(effect.GetType(), out var executor))
                {
                    throw new NotSupportedException(
                        $"EffectDispatcher: no executor registered for '{effect.GetType().Name}'.");
                }
                executor.Execute(effect, runtime);
            }
        }

        public bool HasExecutorFor(Type descriptorType) => _byType.ContainsKey(descriptorType);
    }
}
