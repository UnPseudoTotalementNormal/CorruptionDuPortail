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
            var chat = CompositionRoot.For(runtime.NetworkManager).ChatManager;
            // NET-11: membership is server-owned; a channel is granted to one player (every caller is Specific).
            if (e.Audience.Kind == PowerEffectAudienceKind.Specific)
            {
                chat.GrantChannelServer(e.ChatId, e.ChatName, (ulong)e.Audience.LogicalSlot);
                return;
            }
            chat.DiscoverChatRpc(
                e.ChatId, new FixedString64Bytes(e.ChatName), EffectExecutorHelpers.ResolveTarget(e.Audience, runtime));
        }
    }
}
