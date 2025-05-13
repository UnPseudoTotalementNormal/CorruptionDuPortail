using System;
using System.Linq;
using GameLogic;
using UnityEngine;

namespace UI.SelectPanels
{
    public class SelectPanelPlayer : MonoBehaviour
    {
        private static string _pannelPath = "Prefabs/SelectPanels/SelectPanelPlayer";
        
        [SerializeField] private Transform layoutTransform;
        
        [SerializeField] private GameObject playerButtonPrefab;

        public event Action<GameObject> onPlayerButtonCreated;
        public event Action<ulong> onPlayerButtonClicked;
        
        private void Start()
        {
            foreach (ulong _playerId in GameManager.instance.GetCharacters().Select(character => character.ownerClientId))
            {
                GameObject _playerButton = Instantiate(playerButtonPrefab, layoutTransform);
                PlayerButtonObject _playerButtonObject = _playerButton.GetComponent<PlayerButtonObject>();
                _playerButtonObject.playerId = _playerId;
                _playerButtonObject.onPlayerButtonClicked += OnPlayerButtonClicked;
                onPlayerButtonCreated?.Invoke(_playerButton);
            }
        }

        private void OnPlayerButtonClicked(ulong _playerClicked)
        {
            onPlayerButtonClicked?.Invoke(_playerClicked);
        }

        public static GameObject CreatePannel(Transform _parent)
        {
            GameObject _spawnObject = Resources.Load<GameObject>(_pannelPath);
            GameObject _panel = Instantiate(_spawnObject, _parent);
            return _panel;
        }
    }
}