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
        LobbyPlayerInfoHolder.instance.playerInfos.OnListChanged += OnPlayerInfoListChanged;
        UpdatePanel();
    }

    private void OnPlayerInfoListChanged(NetworkListEvent<PlayerInfo> changeevent)
    {
        UpdatePanel();
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
            newPlayerText.GetComponent<TMP_Text>().text = playerInfo.playerName.ToString();
        }
    }
}
