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
    public bool isPanelOpen { get; private set; }
    
    public void TrySendMessageToServer()
    {
        var _localCharacter = GameManager.instance.characterManager.GetLocalCharacter(false);
        if (_localCharacter.messageLeft.Value <= 0)
        {
            Debug.Log("You have no messages left to send.");
            return;
        }
        
        if (_localCharacter.hasSentMessageThisTurn.Value == true)
        {
            Debug.Log("You have already sent a message this turn.");
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
        var _character = GameManager.instance.characterManager.GetCharacter(_senderId);
        _character.messageLeft.Value -= 1;
        _character.hasSentMessageThisTurn.Value = true;
    }

    public void SwitchPanelOpen()
    {
        if (isPanelOpen)
        {
            ClosePanel();
        }
        else
        {
            TryOpenPanel();
        }
    }

    public void TryOpenPanel()
    {
        if (GameManager.instance.characterManager.GetLocalCharacter(false).messageLeft.Value <= 0)
        {
            Debug.Log("You have no messages left to send.");
            return;
        }
        
        OpenPanel();
        return;
    }

    public void OpenPanel()
    {
        isPanelOpen = true;
        canvasGroup.DoShowGroup(0.5f);
    }

    public void ClosePanel()
    {
        isPanelOpen = false;
        canvasGroup.DoHideGroup(0.5f);
    }
}
