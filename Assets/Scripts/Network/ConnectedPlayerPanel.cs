using System;
using Network;
using Network.Player;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class ConnectedPlayerPanel : MonoBehaviour
{
    [SerializeField] private Transform _playerListParent;
    
    [SerializeField] private GameObject _playerTextObject;

    private void Start()
    {
        LobbyPlayerInfoHolder.playerInfos.OnListChanged += OnPlayerInfoListChanged;
        UpdatePanel();
    }

    private void OnPlayerInfoListChanged(NetworkListEvent<PlayerInfo> changeevent)
    {
        UpdatePanel();
    }

    private void UpdatePanel()
    {
        foreach (Transform child in _playerListParent)
        {
            Destroy(child.gameObject);
        }
        
        foreach (PlayerInfo playerInfo in LobbyPlayerInfoHolder.playerInfos)
        {
            var newPlayerText = Instantiate(_playerTextObject, _playerListParent);
            newPlayerText.SetActive(true);
            newPlayerText.GetComponent<TMP_Text>().text = playerInfo.playerName.ToString();
        }
    }
}
