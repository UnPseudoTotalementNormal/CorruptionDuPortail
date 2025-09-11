#region

using System;
using Network;
using TMPro;
using UnityEngine;

#endregion

namespace UI.SelectPanels
{
    public class PlayerButtonObject : MonoBehaviour
    {
        public ulong playerId;
        
        public event Action<ulong> onPlayerButtonClicked;

        private void Awake()
        {
            GetComponent<CustomButton>().onButtonClicked += () => onPlayerButtonClicked?.Invoke(playerId);
        }

        private void Start()
        {
            foreach (var _playerInfo in LobbyPlayerInfoHolder.instance.playerInfos)
            {
                if (_playerInfo.playerClientId != playerId)
                {
                    continue;
                }
                
                GetComponentInChildren<TMP_Text>().text = _playerInfo.playerName.ToString();
                break;
            }
        }
    }
}