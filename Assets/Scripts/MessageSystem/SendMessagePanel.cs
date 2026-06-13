using System.Text;
using Characters;
using Extensions;
using GameLogic;
using MessageSystem;
using TMPro;
using UI.Panel;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

public class SendMessagePanel : NetworkBehaviour, IPanelComponent
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TMP_InputField messageInputField;
    public bool isPanelOpen { get; private set; }

    // Story 7.4 lane C: NGO-spawned NetworkBehaviour resolves its CharacterManager once from the
    // composition root in OnNetworkSpawn, replacing the GameManager hub-hop and the CharacterManager
    // singleton locator. (The MessageManager singleton stays → Epic 10.)
    // Story 9.1 (Epic 9 / D3): pure-read consumer, resolves the ICharacterQuery slice directly (lane C
    // takes the interface — the composition root hands back the narrowed accessor).
    private ICharacterQuery characterManager;
    // Story 10.4 (Epic 10 / D4): MessageManager resolved once here (lane C) instead of the global.
    // Asserted (not null-tolerant): MessageManager is a GameScene NetworkBehaviour whose Awake-set
    // instance is up before this spawns, and SendMessageRpc dereferences it unconditionally on the
    // local client when the player sends — so a null here is a wiring bug, not a legitimate state.
    private MessageManager messageManager;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        characterManager = CompositionRoot.For(NetworkManager).CharacterQuery;
        Assert.IsNotNull(characterManager, "SendMessagePanel.characterManager could not be resolved from the composition root.");
        messageManager = CompositionRoot.For(NetworkManager).MessageManager;
        Assert.IsNotNull(messageManager, "SendMessagePanel.messageManager could not be resolved from the composition root.");
    }

    public void TrySendMessageToServer()
    {
        var _localCharacter = characterManager.GetLocalCharacter(false);
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
        
        messageManager.SendMessageRpc(characterManager.GetLocalClientId(), messageInputField.text);
        OnMessageSentRpc(characterManager.GetLocalClientId(), messageInputField.text);
        ClosePanel();
    }
    
    [Rpc(SendTo.Server)]
    public void OnMessageSentRpc(ulong _senderId, FixedString512Bytes _message)
    {
        var _character = characterManager.GetCharacter(_senderId);
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
        if (characterManager.GetLocalCharacter(false).messageLeft.Value <= 0)
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
