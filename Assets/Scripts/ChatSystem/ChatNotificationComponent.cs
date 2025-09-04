using System;
using ChatSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChatNotificationComponent : MonoBehaviour
{
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
        canvasGroup.alpha = 0;
        
        chatId = chatWindowId != ChatWindowIDs.Null ? (int)chatWindowId : chatId;
        
        ChatManager.instance.onChatMessageReceived += OnChatMessageReceived;
    }

    private void OnChatMessageReceived(ChatMessage _newChatMessage)
    {
    }
}

public enum ChatNotificationComponentMode
{
    ChatId,
    AllChat,
}
