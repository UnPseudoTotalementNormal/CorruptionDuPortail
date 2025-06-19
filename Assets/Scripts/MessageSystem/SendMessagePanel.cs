using System.Text;
using Extensions;
using GameLogic;
using MessageSystem;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class SendMessagePanel : NetworkBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_InputField messageInputField;
    
    public void TrySendMessageToServer()
    {
        if (Encoding.UTF8.GetByteCount(messageInputField.text) > 512)
        {
            Debug.LogError("Message is too long. Maximum length is 512 bytes.");
            return;
        }
        
        MessageManager.instance.SendMessageRpc(NetworkManager.LocalClientId, messageInputField.text);
        ClosePanel();
    }
    
    [Rpc(SendTo.Server)]
    public void SendMessageRpc(FixedString512Bytes _message)
    {
        Debug.Log($"Message received: {_message}");
    }

    public void OpenPanel()
    {
        canvasGroup.DoShowGroup(0.5f);
    }

    public void ClosePanel()
    {
        canvasGroup.DoHideGroup(0.5f);
    }
}
