using System;
using UnityEngine;

namespace Network.Player
{
    public class LocalPlayerInfoHolder : MonoBehaviour
    {
        public static LocalPlayerInfoHolder Instance { get; private set; }
        
        public PlayerInfo playerInfo { get; set; } = new();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
                return;
            }
            
            InitPlayerInfo();
        }

        private void InitPlayerInfo()
        {
            playerInfo = new PlayerInfo
            {
                playerName = "Player" + UnityEngine.Random.Range(1, 9999)
            };
        }
    }
}