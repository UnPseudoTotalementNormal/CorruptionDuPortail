using Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChatSystem
{
    public class ChatPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text chatTextPrefab;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private RectTransform layoutTransform;
        
        private ChatWindow observedChatWindow;

        private void Start()
        {
            inputField.onEndEdit.AddListener(_text =>
            {
                if (!Input.GetKeyDown(KeyCode.Return)) return;
                TrySendChatMessage(_text);
                inputField.text = string.Empty;
            });
            OnActiveChatChanged(ChatManager.instance.activeChatId);
            ChatManager.instance.onActiveChatChanged += OnActiveChatChanged;
        }
        
        private void TrySendChatMessage(string _text)
        {
            ChatManager.instance.TrySendChatMessage(_text);
        }

        private void OnActiveChatChanged(int _newId)
        {
            ChatWindow _oldObservedChat = observedChatWindow;
            ChatWindow _newObservedChat = ChatManager.instance.GetChatWindow(_newId);
            
            if (_oldObservedChat != null)
            {
                _oldObservedChat.onMessageReceived -= OnMessageReceived;
            }
            
            if (_newObservedChat != null)
            {
                _newObservedChat.onMessageReceived += OnMessageReceived;
            }
            
            RedrawChatMessages();
            
            observedChatWindow = _newObservedChat;
        }

        private void RedrawChatMessages()
        {
            foreach (Transform child in layoutTransform)
            {
                Destroy(child.gameObject);
            }

            if (observedChatWindow != null)
            {
                foreach (var _message in observedChatWindow.chatMessages)
                {
                    AddMessage(_message);
                }
            }
        }
        
        private void OnMessageReceived(ChatMessage _newMessage)
        {
            AddMessage(_newMessage);
        }
        
        public void AddMessage(ChatMessage _chatMessage)
        {
            string _senderName = _chatMessage.senderClientId == GameValues.CHAT_SERVER_CLIENT_ID 
                ? "Server" 
                : LobbyPlayerInfoHolder.instance.GetPlayerInfo(_chatMessage.senderClientId).playerName.ToString();
            
            TMP_Text _chatText = Instantiate(chatTextPrefab, layoutTransform);

            _chatText.text = $"{_senderName}: {_chatMessage.message}";

            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutTransform);
        }
    }
}