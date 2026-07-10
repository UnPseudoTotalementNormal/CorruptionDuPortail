using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class RequestCharacterRefreshExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(RequestCharacterRefresh);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            CompositionRoot.For(runtime.NetworkManager).CharacterManager.AskForUpdateAllCharactersRpc();
        }
    }
}
