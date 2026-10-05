using System;
using ChatSystem;
using CorruptionDuPortail.Domain;
using GameLogic;

namespace Characters.Powers.Runtime.Executors
{
    public sealed class ChatLocalExecutor : IEffectExecutor
    {
        public Type DescriptorType => typeof(ChatLocal);

        public void Execute(EffectDescriptor effect, EffectRuntime runtime)
        {
            var e = (ChatLocal)effect;
            var chat = CompositionRoot.For(runtime.NetworkManager).ChatManager;

            // NET-09: a decision running on the SERVER for a specific player delivers its "local" line to that player.
            if (runtime.RoutesToViewer && chat != null && chat.IsSpawned)
            {
                var message = new ChatMessage(ChatManager.SERVER_CLIENT_ID, e.Message, e.WindowId);
                chat.ReceiveChatMessageRpc(message,
                    CompositionRoot.For(runtime.NetworkManager).CharacterManager.GetSafeRpcTarget(runtime.LocalViewer.Value));
                return;
            }

            chat.AddMessageLocal(e.Message, ChatManager.SERVER_CLIENT_ID, e.WindowId);
        }
    }
}
