using System;
using MessageSystem;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace UI
{
    public class AnonymousRevealedMessagesComponent : MonoBehaviour
    {
        public Transform messagesLayoutTransform;
        public GameObject anonymousMessagePrefab;
        
        private void Start()
        {
            MessageManager.instance.revealedMessages.OnListChanged += OnRevealedMessagesChanged;
        }

        private void OnRevealedMessagesChanged(NetworkListEvent<MessageInfo> _changeEvent)
        {
            RebuildRevealedMessagesUI();
        }

        private void RebuildRevealedMessagesUI()
        {
            foreach (GameObject _messageObject in messagesLayoutTransform)
            {
                Destroy(_messageObject);
            }
            
            int _currentDay = -1;
            int _messageRevealedTodayCount = 1;
            foreach (var _revealedMessage in MessageManager.instance.revealedMessages)
            {
                if (_revealedMessage.day != _currentDay)
                {
                    _currentDay = _revealedMessage.day;
                    _messageRevealedTodayCount = 1;
                    SpawnNewMessageText($"Messages du jour {_currentDay}:").GetComponent<TMP_Text>().fontStyle |= FontStyles.Underline;
                }
                SpawnNewMessageText($"{_messageRevealedTodayCount}: \"{_revealedMessage.message.ToString()}\"");
                _messageRevealedTodayCount += 1;
            }
        }
        
        private GameObject SpawnNewMessageText(string _text)
        {
            GameObject _newMessageObject = Instantiate(anonymousMessagePrefab, messagesLayoutTransform, false);

            var _messageText = _newMessageObject.GetComponent<TMP_Text>();
            _messageText.text = _text;
            
            return _newMessageObject;
        }
    }
}