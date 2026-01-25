using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic;
using TMPro;
using UI.TableSystem;
using UnityEngine;
using UnityEngine.UI;

namespace UI.InfoTable
{
    public class InfoTableSystem : MonoBehaviour
    {
        [SerializeField] private HorizontalLayoutGroup rowPrefab;
        [SerializeField] private Header headerPrefab;
        [SerializeField] private ChildHeader childHeaderPrefab;
        [SerializeField] private Transform contentRoot;

        private void Start()
        {
            GameManager.instance.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            Clean();
            BuildGameUi();
        }

        private void BuildGameUi()
        {
            // Create Headers Row
            HorizontalLayoutGroup _headersRow = Instantiate(rowPrefab, contentRoot);
            _headersRow.gameObject.name = "Headers Row";

            // Create individual Headers
            Header _playerRoleHeader = Instantiate(headerPrefab, _headersRow.transform);
            _playerRoleHeader.headerText.SetText("Joueurs / Rôles");
            
            // Create role headers
            Dictionary<Role, int> _roleCounts = new();
            foreach (Character _character in CharacterManager.instance.GetCharacters(false))
            {
                if (_character.role == null)
                {
                    continue;
                }

                KeyValuePair<Role, int> _keyValuePair = _roleCounts.FirstOrDefault(_rc => _rc.Key.IsTheSameRole(_character.role));
                if (_keyValuePair.Key != null)
                {
                    _roleCounts[_keyValuePair.Key]++;
                }
                else
                {
                    _roleCounts[_character.role] = 1;
                }
            }
            
            foreach (KeyValuePair<Role, int> _roleCount in _roleCounts)
            {
                Header _roleHeader = Instantiate(headerPrefab, _headersRow.transform);
                _roleHeader.headerText.SetText($"{_roleCount.Key.roleName}{(_roleCount.Value > 1 ? $" *{_roleCount.Value}" : "")}");
                _roleHeader.gameObject.AddComponent<ChildHeader>().SetVerticalHeader(_playerRoleHeader);
                _roleHeader.layoutElement.preferredWidth = 100;
            }
            
            // Create individual Player Rows
            foreach (Character _character in CharacterManager.instance.GetCharacters(false).Where(_c => _c.isFake == false))
            {
                HorizontalLayoutGroup _playerRow = Instantiate(rowPrefab, contentRoot);
                _playerRow.gameObject.name = $"Player Row - {_character.GetOwnerPseudo()}";

                // Create Player Name Cell
                Header _playerNameCell = Instantiate(headerPrefab, _playerRow.transform);
                _playerNameCell.headerText.SetText(_character.GetOwnerPseudo());
                _playerNameCell.gameObject.AddComponent<ChildHeader>().SetHorizontalHeader(_playerRoleHeader);
                _playerNameCell.layoutElement.preferredHeight = 100;

                // Create Role Cells
                int _roleIndex = 0;
                foreach (KeyValuePair<Role, int> _roleCount in _roleCounts)
                {
                    ChildHeader _roleCell = Instantiate(childHeaderPrefab, _playerRow.transform);
                    if (_character.role != null && _character.role.IsTheSameRole(_roleCount.Key))
                    {
                        _roleCell.GetComponentInChildren<TMP_Text>().SetText("X");
                    }
                    else
                    {
                        _roleCell.GetComponentInChildren<TMP_Text>().SetText("");
                    }
                    
                    Header _header = contentRoot.GetChild(0).GetChild(_roleIndex).GetComponent<Header>();
                    _roleCell.SetHorizontalHeader(_header);
                    _roleCell.SetVerticalHeader(_playerNameCell);
                    _roleIndex++;
                }
            }
        }
        
        private void Clean()
        {
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(contentRoot.GetChild(i).gameObject);
            }
        }
    }
}
