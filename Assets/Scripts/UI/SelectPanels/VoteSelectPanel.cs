using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.GameStates;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UI;

namespace UI.SelectPanels
{
    public class VoteSelectPanel : MonoBehaviour
    {
        public VoteState voteState;
        
        public event Action<ulong> onPlayerVoted;

        private event Action onDestroy;
        
        private void Awake()
        {
            var _selectPanelPlayer = GetComponent<SelectPanelPlayer>();
            _selectPanelPlayer.onPlayerButtonCreated += OnPlayerButtonCreated;
            _selectPanelPlayer.onPlayerButtonClicked += OnPlayerButtonClicked;
        }

        private void OnPlayerButtonClicked(ulong _clientId)
        {
            onPlayerVoted?.Invoke(_clientId);
        }

        private void OnPlayerButtonCreated(GameObject _playerButton)
        {
            var _playerButtonObject = _playerButton.GetComponent<PlayerButtonObject>();
            if (GameManager.instance.characters.First(_character => _character.ownerClientId == _playerButtonObject.playerId).isChained)
            {
                Destroy(_playerButton);
                return;
            }
            
            var _newPlayerButtonParent = new GameObject("PlayerButtonParent", typeof(RectTransform), typeof(VerticalLayoutGroup));
            _newPlayerButtonParent.transform.SetParent(_playerButton.transform.parent);
            
            _newPlayerButtonParent.GetComponent<RectTransform>().sizeDelta = _playerButton.GetComponent<RectTransform>().sizeDelta;
            
            var _verticalLayoutGroup = _newPlayerButtonParent.GetComponent<VerticalLayoutGroup>();
            _verticalLayoutGroup.childControlHeight = true;
            _verticalLayoutGroup.childControlWidth = true;
            _verticalLayoutGroup.childScaleHeight = false;
            _verticalLayoutGroup.childScaleWidth = false;
            _verticalLayoutGroup.childForceExpandHeight = false;
            _verticalLayoutGroup.childForceExpandWidth = true;
            _verticalLayoutGroup.childAlignment = TextAnchor.MiddleCenter;
            
            _playerButton.transform.SetParent(_newPlayerButtonParent.transform);
            
            var _playerVoteCounter = new GameObject("PlayerVoteCounter", typeof(RectTransform));
            _playerVoteCounter.transform.SetParent(_newPlayerButtonParent.transform);
            _playerVoteCounter.transform.SetAsLastSibling();
            
            var _voteText = _playerVoteCounter.AddComponent<TextMeshProUGUI>();
            _voteText.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Center;
            _voteText.verticalAlignment = TMPro.VerticalAlignmentOptions.Middle;
            _voteText.text = "Votes: 0";
            
            Action<Dictionary<ulong, List<ulong>>> voteRefreshHandler = (_voteDictionary) =>
            {
                UpdateVoteCount(_playerButton, _voteDictionary, _voteText);
            };

            voteState.onVoteRefresh += voteRefreshHandler;
            onDestroy += () =>
            {
                voteState.onVoteRefresh -= voteRefreshHandler;
            };
        }

        private void UpdateVoteCount(GameObject _playerButton, Dictionary<ulong, List<ulong>> _voteDictionary, TextMeshProUGUI _voteText)
        {
            if (_voteDictionary == null || !_playerButton)
            {
                return;
            }
            
            if (_voteDictionary.TryGetValue(_playerButton.GetComponent<PlayerButtonObject>().playerId, out var _votes))
            {
                _voteText.text = "Votes: " + _votes.Count;
            }
            else
            {
                _voteText.text = "Votes: 0";
            }
        }

        private void OnDestroy()
        {
            onDestroy?.Invoke();
        }
    }
}
