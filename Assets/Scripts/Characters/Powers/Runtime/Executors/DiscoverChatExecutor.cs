using System;
using ChatSystem;
using GameLogic;
using Unity.Collections;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class DiscoverChatExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(DiscoverChat);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (DiscoverChat)effect;
            CompositionRoot.For(runtime.NetworkManager).ChatManager.DiscoverChatRpc(
                e.ChatId, new FixedString64Bytes(e.ChatName), EffectExecutorHelpers.ResolveTarget(e.Audience, runtime));
        }
    }
}
