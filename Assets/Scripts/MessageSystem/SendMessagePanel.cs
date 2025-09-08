using System.Text;
using Extensions;
using GameLogic;
using MessageSystem;
using TMPro;
using UI.Panel;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class SendMessagePanel : NetworkBehaviour, IPanelComponent
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_InputField messageInputField;
    
    public void TrySendMessageToServer()
    {
        if (GameManager.instance.GetLocalCharacter(false).messageLeft <= 0)
        {
            Debug.Log("You have no messages left to send.");
            return;
        }
        
        if (Encoding.UTF8.GetByteCount(messageInputField.text) > 512)
        {
            Debug.Log("Message is too long. Maximum length is 512 bytes.");
            return;
        }
        
        MessageManager.instance.SendMessageRpc(NetworkManager.LocalClientId, messageInputField.text);
        OnMessageSentRpc(NetworkManager.LocalClientId, messageInputField.text);
        ClosePanel();
    }
    
    [Rpc(SendTo.Server)]
    public void OnMessageSentRpc(ulong _senderId, FixedString512Bytes _message)
    {
        GameManager.instance.GetCharacter(_senderId).messageLeft -= 1;
        GameManager.instance.AskForUpdateAllCharactersRpc();
    }

    public void TryOpenPanel()
    {
        if (GameManager.instance.GetLocalCharacter(false).messageLeft <= 0)
        {
            Debug.Log("You have no messages left to send.");
            return;
        }
        
        OpenPanel();
        return;
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
