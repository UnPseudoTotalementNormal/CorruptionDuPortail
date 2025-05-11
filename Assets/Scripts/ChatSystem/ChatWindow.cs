using System;
using GameLogic;
using Network;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace ChatSystem
{
    public class ChatWindow : MonoBehaviour
    {
        private ChatManager chatManager;
        [SerializeField] private TMP_Text chatTextPrefab;
    
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private RectTransform layoutTransform;

        private void Awake()
        {
            inputField.onEndEdit.AddListener(_text =>
            {
                if (Input.GetKeyDown(KeyCode.Return))
                {
                    TrySendChatMessage(_text);
                    inputField.text = string.Empty;
                }
            });
        }

        private void TrySendChatMessage(string _text)
        {
            SendChatMessage(_text);
        }

        private void SendChatMessage(string _text)
        {
            if (string.IsNullOrEmpty(_text))
                return;

            FixedString512Bytes _message = new FixedString512Bytes(_text);
            chatManager.SendChatMessageServerRpc(_message, NetworkManager.Singleton.LocalClientId);
        }

        public void SetChatManager(ChatManager _chatManager)
        {
            chatManager = _chatManager;
        }

        public void AddMessage(FixedString512Bytes _message, string _senderName)
        {
            TMP_Text _chatText = Instantiate(chatTextPrefab, layoutTransform);

            _chatText.text = $"{_senderName}: {_message.ToString()}";

            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutTransform);
        }


    }
}
