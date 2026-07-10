using System;
using ChatSystem;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class ChatBroadcastExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(ChatBroadcast);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (ChatBroadcast)effect;
            var chat = CompositionRoot.For(runtime.NetworkManager).ChatManager;
            var message = new ChatMessage(ChatManager.SERVER_CLIENT_ID, e.Message, e.WindowId);
            if (e.Audience.Kind == PowerEffectAudienceKind.All)
            {
                chat.ReceiveChatMessageRpc(message);
            }
            else
            {
                chat.ReceiveChatMessageRpc(message, EffectExecutorHelpers.ResolveTarget(e.Audience, runtime));
            }
        }
    }
}
