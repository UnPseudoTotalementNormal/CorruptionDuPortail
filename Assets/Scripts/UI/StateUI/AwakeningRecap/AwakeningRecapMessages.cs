using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using GameLogic;
using GameLogic.GameStates;
using MessageSystem;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Components
{
    public class AwakeningRecapMessages : AwakeningRecapEventComponent
    {
        public float timeAddedPerMessage = 15f;
        
        [ReadOnly] public Stack<MessageInfo> messagesToReveal = new();
        public TMP_Text titleText;
        public TMP_Text messageText;
        
        public CanvasGroup messagesCanvasGroup;
        public Transform messagesLayoutTransform;
        
        public GameObject anonymousMessagePrefab;
        
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
            messagesCanvasGroup.alpha = 0;
            await BasePanel();

            messagesCanvasGroup.DOFade(1, 0.5f);
            titleText.text = $"Message anonyme";

            SpawnNewMessageText($"Messages du jour {GameManager.instance.currentDay}:").GetComponent<TMP_Text>().fontStyle |= FontStyles.Underline;

            int _messageRevealedCount = 0;
            while (messagesToReveal.Count > 0)
            {
                MessageInfo _messageInfo = messagesToReveal.Pop();
                
                _messageRevealedCount += 1;
                var _newMessageObject = SpawnNewMessageText($"{_messageRevealedCount}: \"{_messageInfo.message.ToString()}\"");
                
                CanvasGroup _messageCanvasGroup = _newMessageObject.GetComponent<CanvasGroup>();
                _messageCanvasGroup.alpha = 0;
                _messageCanvasGroup.DOFade(1, 0.5f);

                await UniTask.Delay(TimeSpan.FromSeconds(timeAddedPerMessage));
            }
        }

        private GameObject SpawnNewMessageText(string _text)
        {
            GameObject _newMessageObject = Instantiate(anonymousMessagePrefab, messagesLayoutTransform, false);

            var _messageText = _newMessageObject.GetComponent<TMP_Text>();
            _messageText.text = _text;
            
            return _newMessageObject;
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