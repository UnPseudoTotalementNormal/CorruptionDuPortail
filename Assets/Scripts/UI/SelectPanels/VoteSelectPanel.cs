using System;
using GameLogic.GameStates;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.SelectPanels
{
    public class VoteSelectPanel : MonoBehaviour
    {
        public VoteState voteState;
        
        public event Action<ulong> onPlayerVoted;
        
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
            voteState.onVoteRefresh += (_voteDictionary =>
            {
                if (_voteDictionary.TryGetValue(_playerButton.GetComponent<PlayerButtonObject>().playerId, out var _votes))
                {
                    _voteText.text = "Votes: " + _votes.Count;
                }
                else
                {
                    _voteText.text = "Votes: 0";
                }
            });
        }
    }
}
