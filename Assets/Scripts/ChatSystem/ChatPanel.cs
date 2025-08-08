using System.Collections;
using DG.Tweening;
using Network;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace ChatSystem
{
    public class ChatPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text chatTextPrefab;
        [SerializeField] private TMP_Text chatTitleText;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private RectTransform layoutTransform;
        [SerializeField] private RectTransform discoveredChatLayoutTransform;
        [SerializeField] private Scrollbar chatScrollbar;
        
        [SerializeField] private CustomButton discoveredChatButtonPrefab;
        
        [SerializeField]private float scrollbarBottomThreshold = 0.05f;
        private ChatWindow observedChatWindow;
        
        private bool isFullScreen = false;
        private Vector2 baseSizeDelta;
        [SerializeField] private float fullScreenSwitchDuration = 0.5f;
        
        private void Start()
        {
            baseSizeDelta = GetComponent<RectTransform>().sizeDelta;
            inputField.onEndEdit.AddListener(_text =>
            {
                if (!Input.GetKeyDown(KeyCode.Return)) return;
                TrySendChatMessage(_text);
                inputField.text = string.Empty;
            });
            OnActiveChatChanged(ChatManager.instance.activeChatId);
            ChatManager.instance.onActiveChatChanged += OnActiveChatChanged;
            ChatManager.instance.onChatDiscovered += OnChatDiscovered;
            foreach (var _discoveredChatId in ChatManager.instance.discoveredChatIds)
            {
                OnChatDiscovered(_discoveredChatId);
            }
        }

        private void OnChatDiscovered(int _chatId)
        {
            var _newButton = Instantiate(discoveredChatButtonPrefab, discoveredChatLayoutTransform);
            _newButton.GetComponentInChildren<TMP_Text>().text = ChatManager.instance.GetChatWindow(_chatId).chatName.ToString();
            _newButton.onButtonClicked += () =>
            {
                ChatManager.instance.ChangeActiveChat(_chatId);
            };
        }

        private void TrySendChatMessage(string _text)
        {
            ChatManager.instance.TrySendChatMessage(_text);
        }

        private void OnActiveChatChanged(int _newId)
        {
            ChatWindow _oldObservedChat = observedChatWindow;
            ChatWindow _newObservedChat = ChatManager.instance.GetChatWindow(_newId);
            observedChatWindow = _newObservedChat;
            
            if (_oldObservedChat != null)
            {
                _oldObservedChat.onMessageReceived -= OnMessageReceived;
            }
            
            if (_newObservedChat != null)
            {
                _newObservedChat.onMessageReceived += OnMessageReceived;
            }
            
            RedrawChatMessages();
            chatTitleText.text = _newObservedChat.chatName.ToString();
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
            float _oldScrollbarValue = chatScrollbar.value;
            
            string _senderName = _chatMessage.senderClientId == GameValues.CHAT_SERVER_CLIENT_ID 
                ? "Server" 
                : LobbyPlayerInfoHolder.instance.GetPlayerInfo(_chatMessage.senderClientId).playerName.ToString();
            
            TMP_Text _chatText = Instantiate(chatTextPrefab, layoutTransform);

            _chatText.text = $"{_senderName}: {_chatMessage.message}";

            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutTransform);
            
            if (_oldScrollbarValue < scrollbarBottomThreshold)
            {
                StartCoroutine(WaitForScrollbarUpdate());
            }
        }
        
        private IEnumerator WaitForScrollbarUpdate()
        {
            yield return new WaitForEndOfFrame();
            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutTransform);
            chatScrollbar.value = 0f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(layoutTransform);
        }
        
        public void OnFullScreenButtonClicked()
        {
            if (isFullScreen)
            {
                DeactivateFullScreen();
            }
            else
            {
                ActivateFullScreen();
            }
        }

        public void ActivateFullScreen()
        {
            if (isFullScreen)
            {
                return;
            }
            
            isFullScreen = true;
            var _canvasRectTransform = GetComponentInParent<Canvas>().GetComponent<RectTransform>();
            Vector2 _canvasSizeDelta = _canvasRectTransform.sizeDelta;

            GetComponent<RectTransform>().DOSizeDelta(_canvasSizeDelta, fullScreenSwitchDuration).SetEase(Ease.OutQuint);
        }
        
        public void DeactivateFullScreen()
        {
            if (!isFullScreen)
            {
                return;
            }
            
            isFullScreen = false;
            
            GetComponent<RectTransform>().DOSizeDelta(baseSizeDelta, fullScreenSwitchDuration).SetEase(Ease.OutQuint);
        }
    }
}