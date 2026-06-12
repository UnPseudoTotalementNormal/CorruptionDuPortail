#region

using System;
using System.Linq;
using Characters;
using GameLogic;
using UI.SelectPanels;
using UnityEngine;

#endregion

namespace UI.SpawnPanels
{
    public class SelectPanelPlayer : MonoBehaviour
    {
        private static string _pannelPath = "Prefabs/SpawnPanels/SelectPanelPlayer";
        
        [SerializeField] private Transform layoutTransform;
        
        [SerializeField] private GameObject playerButtonPrefab;

        public event Action<GameObject> onPlayerButtonCreated;
        public event Action<ulong> onPlayerButtonClicked;
        
        private void Start()
        {
            // Story 7.4: façade route (CharacterManager.instance) — UI leaf, proper injection deferred to Epic 12.
            foreach (ulong _playerId in CharacterManager.instance.GetCharacters().Where(_c => !_c.isFake).Select(_character => _character.ownerClientId.Value))
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