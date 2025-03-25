using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class ConnectedPlayerPanel : MonoBehaviour
{
    [SerializeField] private Transform _playerListParent;
    
    [SerializeField] private GameObject _playerTextObject;
    
    private void Update()
    {
        foreach (Transform child in _playerListParent)
        {
            Destroy(child.gameObject);
        }
        
        
        foreach (var client in NetworkManager.Singleton.ConnectedClients.Values)
        {
            var newPlayerText = Instantiate(_playerTextObject, _playerListParent);
            newPlayerText.SetActive(true);
            newPlayerText.GetComponent<TMP_Text>().text = client.ClientId.ToString();
        }
    }
}
