using System;
using ChatSystem;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class ChatSendServerExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(ChatSendServer);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (ChatSendServer)effect;
            CompositionRoot.For(runtime.NetworkManager).ChatManager
                .SendChatMessageServerRpc(new ChatMessage(ChatManager.SERVER_CLIENT_ID, e.Message, e.WindowId));
        }
    }
}
