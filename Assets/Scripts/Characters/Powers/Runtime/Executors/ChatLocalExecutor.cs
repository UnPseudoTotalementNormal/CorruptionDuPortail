using System;
using ChatSystem;
using GameLogic;
using CorruptionDuPortail.Domain;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class ChatLocalExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(ChatLocal);
        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (ChatLocal)effect;
            CompositionRoot.For(runtime.NetworkManager).ChatManager
                .AddMessageLocal(e.Message, ChatManager.SERVER_CLIENT_ID, e.WindowId);
        }
    }
}
