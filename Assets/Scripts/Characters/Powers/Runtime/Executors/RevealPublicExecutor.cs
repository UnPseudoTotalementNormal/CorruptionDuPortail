using System;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class RevealPublicExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(RevealPublic);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (RevealPublic)effect;
            CompositionRoot.For(runtime.NetworkManager).GameInfoRevealer.SetRevealLevelRpc(
                (ulong)e.TargetSlot, EffectExecutorHelpers.RevealFieldName(e.Field), RevealLevel.Public, true);
        }
    }
}
