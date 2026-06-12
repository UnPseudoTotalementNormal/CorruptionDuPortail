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
    // Story 12.3: a presentation view over the MessageManager singleton's reveal list — MessageManager is a
    // recorded §4 survivor singleton (not de-singletonised; served from its one instance), so those reads stay.
    // The GameManager.currentDay read is rerouted onto the sanctioned CompositionRoot.For(Singleton). The base's
    // pushed CharacterQuery is unused by this leaf.
    public class AwakeningRecapMessages : AwakeningRecapEventComponent
    {
        [Header("Timing Configuration")]
        public float baseTimePerMessage = 2f; 
        public float timePerCharacter = 0.05f; 
        public float minMessageDisplayTime = 3f;
        public float maxMessageDisplayTime = 10f; 
        
        [ReadOnly] public Stack<MessageInfo> messagesToReveal = new();
        public TMP_Text titleText;
        public TMP_Text messageText;
        
        public CanvasGroup messagesCanvasGroup;
        public Transform messagesLayoutTransform;
        
        public GameObject anonymousMessagePrefab;
        
        /// <summary>
        /// Calcule le temps d'affichage pour un message en fonction de sa longueur
        /// </summary>
        private float CalculateMessageDisplayTime(string _message)
        {
            float _calculatedTime = baseTimePerMessage + (_message.Length * timePerCharacter);
            return Mathf.Clamp(_calculatedTime, minMessageDisplayTime, maxMessageDisplayTime);
        }
        
        public override float EvaluateDuration()
        {
            float _totalTime = baseDuration;
            
            foreach (var _messageInfo in MessageManager.instance.messagesToReveal)
            {
                _totalTime += CalculateMessageDisplayTime(_messageInfo.message.ToString());
            }
            
            return _totalTime;
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

            SpawnNewMessageText($"Messages du jour {CompositionRoot.For(NetworkManager.Singleton).GameManager.currentDay}:").GetComponent<TMP_Text>().fontStyle |= FontStyles.Underline;

            int _messageRevealedCount = 0;
            while (messagesToReveal.Count > 0)
            {
                MessageInfo _messageInfo = messagesToReveal.Pop();
                
                _messageRevealedCount += 1;
                string _messageContent = _messageInfo.message.ToString();
                var _newMessageObject = SpawnNewMessageText($"{_messageRevealedCount}: \"{_messageContent}\"");
                
                CanvasGroup _messageCanvasGroup = _newMessageObject.GetComponent<CanvasGroup>();
                _messageCanvasGroup.alpha = 0;
                _messageCanvasGroup.DOFade(1, 0.5f);
                
                float _displayTime = CalculateMessageDisplayTime(_messageContent);
                await UniTask.Delay(TimeSpan.FromSeconds(_displayTime));
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