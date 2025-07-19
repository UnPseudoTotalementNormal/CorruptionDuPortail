using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using GameLogic.GameStates;
using MessageSystem;
using TMPro;
using Unity.Collections;
using Unity.Netcode;

namespace UI.Components
{
    public class AwakeningRecapMessages : AwakeningRecapEventComponent
    {
        public float timeAddedPerMessage = 15f;
        
        [ReadOnly] public Stack<MessageInfo> messagesToReveal = new();
        public TMP_Text titleText;
        public TMP_Text messageText;
        
        public override float EvaluateDuration()
        {
            int _messageCount = MessageManager.instance.messagesToReveal.Count;
            return baseDuration + (_messageCount * timeAddedPerMessage);
        }
        
        public override void SetupEvent(AwakeningRecapEvent _recapEvent)
        {
            base.SetupEvent(_recapEvent);
        }

        public override void ShowEvent()
        {
            base.ShowEvent();
            SetupMessagesToReveal();
            _ = RevealMessagesAsync();
        }

        private async UniTaskVoid RevealMessagesAsync()
        {
            await BasePanel();
            
            messageText.horizontalAlignment = HorizontalAlignmentOptions.Justified;
            while (messagesToReveal.Count > 0)
            {
                messageText.DOFade(1, 0.5f);
                
                MessageInfo _messageInfo = messagesToReveal.Pop();
                titleText.text = $"Message anonyme";
                messageText.text =  $"\"{_messageInfo.message.ToString()}\"";
                
                messageText.DOFade(0, 0.5f).SetDelay(timeAddedPerMessage - 0.5f);
                await UniTask.Delay(TimeSpan.FromSeconds(timeAddedPerMessage));
            }
        }

        private async UniTask BasePanel()
        {
            messageText.DOFade(1, 0.5f);
            
            titleText.text = $"Message anonyme";
            messageText.horizontalAlignment = HorizontalAlignmentOptions.Center;
            if (messagesToReveal.Count == 1)
            {
                messageText.text = $"Durant cet éveil, 1 message a été envoyé.";
            }
            else if (messagesToReveal.Count > 1)
            {
                messageText.text = $"Durant cet éveil, {messagesToReveal.Count} messages ont été envoyés.";
            }
            else
            {
                messageText.text = $"Durant cet éveil, aucun message n'a été envoyé.";
            }

            messageText.DOFade(0, 0.5f).SetDelay(baseDuration - 0.5f);
            await UniTask.Delay(TimeSpan.FromSeconds(baseDuration));
        }
        
        private void SetupMessagesToReveal()
        {
            messagesToReveal = new Stack<MessageInfo>();
            foreach (var _messageInfo in MessageManager.instance.messagesToReveal)
            {
                messagesToReveal.Push(_messageInfo);
            }

            if (NetworkManager.Singleton.IsServer)
            {
                MessageManager.instance.RevealAllMessage();
            }
        }
        
        public override void HideEvent()
        {
            base.HideEvent();
        }
    }
}