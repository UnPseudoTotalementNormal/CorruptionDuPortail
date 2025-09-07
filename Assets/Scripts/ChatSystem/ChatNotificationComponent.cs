using System;
using ChatSystem;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatNotificationComponent : MonoBehaviour
{
    private ChatPanel chatPanel;
    public CanvasGroup canvasGroup;
    public Image notificationIcon;
    public TMP_Text notificationText;
    public ChatNotificationComponentMode notificationMode = ChatNotificationComponentMode.ChatId;
    
    public int chatId;
    
    [Tooltip("Will override chatId if not Null")]
    [SerializeField] private ChatWindowIDs chatWindowId = ChatWindowIDs.Null;
    
    private int notificationCount = 0;
    

    private void Awake()
    {
        chatPanel = GetComponentInParent<ChatPanel>();
        
        canvasGroup.alpha = 0;
        
        chatId = chatWindowId != ChatWindowIDs.Null ? (int)chatWindowId : chatId;
        
        ChatManager.instance.onChatMessageReceived += OnChatMessageReceived;
        chatPanel.onScrollbarBottomReached += OnScrollbarBottomReached;
        ChatManager.instance.onActiveChatChanged += OnActiveChatChanged;
    }

    private void OnActiveChatChanged(int _newChatId)
    {
        if (notificationMode == ChatNotificationComponentMode.ChatId && _newChatId == chatId)
        {
            SetNotificationCount(0);
        }
    }

    private void OnScrollbarBottomReached()
    {
        if (ChatManager.instance.activeChatId == chatId && notificationCount > 0)
        {
            SetNotificationCount(0);
        }
    }

    private void OnChatMessageReceived(ChatMessage _newChatMessage)
    {
        var _chatManager = ChatManager.instance;
        if (notificationMode == ChatNotificationComponentMode.ChatId && _newChatMessage.chatId != chatId)
        {
            return;
        }
        
        if (_chatManager.activeChatId == _newChatMessage.chatId && chatPanel.isScrollbarAtBottom)
        {
            return;
        }
        
        SetNotificationCount(notificationCount + 1);
    }

    private void SetNotificationCount(int _count)
    {
        notificationCount = _count;
        notificationText.text = _count.ToString();
        canvasGroup.DOFade(_count > 0 ? 1 : 0, 0.35f);
        if (_count > 0)
        {
            notificationIcon.transform.DOKill(true);
            notificationIcon.transform.DOPunchScale(Vector3.one * 0.15f, 0.5f, 1, 0.2f);
        }
    }
}

public enum ChatNotificationComponentMode
{
    ChatId,
    AllChat,
}
