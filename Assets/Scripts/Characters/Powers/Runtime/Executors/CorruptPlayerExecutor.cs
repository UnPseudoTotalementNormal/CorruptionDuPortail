using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    /// <summary>
    /// Executes a <see cref="CorruptPlayer"/>: corrupts the character at the slot via its own
    /// server RPC. CharacterManager resolved per-NetworkManager via CompositionRoot.
    /// </summary>
    public sealed class CorruptPlayerExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(CorruptPlayer);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (CorruptPlayer)effect;
            CompositionRoot.For(runtime.NetworkManager).CharacterManager
                .GetCharacter((ulong)e.Slot, false).CorruptPlayerServerRpc();
        }
    }
}
