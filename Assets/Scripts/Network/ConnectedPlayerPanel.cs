#region

using Network;
using Network.Player;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

#endregion

public class ConnectedPlayerPanel : MonoBehaviour
{
    [FormerlySerializedAs("_playerListParent")] [SerializeField] private Transform playerListParent;
    
    [FormerlySerializedAs("_playerTextObject")] [SerializeField] private GameObject playerTextObject;
    [SerializeField] private TMP_Text playerCountText;
    private void Start()
    {
        LobbyPlayerInfoHolder.instance.onRosterChanged += UpdatePanel;
        UpdatePanel();
    }

    private void OnDestroy()
    {
        if (LobbyPlayerInfoHolder.instance != null)
        {
            LobbyPlayerInfoHolder.instance.onRosterChanged -= UpdatePanel;
        }
    }

    private void UpdatePanel()
    {
        foreach (Transform child in playerListParent)
        {
            Destroy(child.gameObject);
        }

        var _playerInfos = LobbyPlayerInfoHolder.instance.playerInfos;
        
        playerCountText.text = $"Connected Players: {_playerInfos.Count.ToString()}";
        
        foreach (PlayerInfo playerInfo in _playerInfos)
        {
            var newPlayerText = Instantiate(playerTextObject, playerListParent);
            newPlayerText.SetActive(true);
            var _tmpText = newPlayerText.GetComponent<TMP_Text>();
            _tmpText.text = playerInfo.playerName.ToString();
            _tmpText.enableAutoSizing = false;
            _tmpText.fontSize = 27;
        }
    }
}
