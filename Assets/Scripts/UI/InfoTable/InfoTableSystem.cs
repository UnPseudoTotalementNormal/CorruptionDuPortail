using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic;
using UI.TableSystem;
using UnityEngine;
using UnityEngine.UI;

namespace UI.InfoTable
{
    public class InfoTableSystem : MonoBehaviour
    {
        [SerializeField] private HorizontalLayoutGroup rowPrefab;
        [SerializeField] private Header headerPrefab;
        [SerializeField] private Header playerHeaderPrefab;
        [SerializeField] private ChildHeader roleCheckPrefab;
        [SerializeField] private Transform contentRoot;
        // Story 7.4 lane A: scene-wired, replacing the GameManager hub-hop. Null-tolerant — code keeps its own null-checks; SceneWiringGuard CI is the wiring control (no Assert here).
        [SerializeField] private GameInfoRevealer gameInfoRevealer;
        // Story 12.2 lane A: GameManager + CharacterManager scene-wired, clearing the §4a-entangled hub reads. SceneWiringGuard is the wiring control.
        [SerializeField] private GameManager gameManager;
        [SerializeField] private CharacterManager characterManager;

        private List<InfoTablePlayerRoleHandler> playerHandlers = new();
        private Dictionary<Role, int> roleCounts = new();

        private void Start()
        {
            gameManager.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            Clean();
            BuildGameUi();
            
            // S'abonner aux changements de révélation de rôles
            if (gameInfoRevealer != null)
            {
                gameInfoRevealer.onCharacterInfoRevealedChanged += OnCharacterInfoRevealedChanged;
            }
        }

        private void OnCharacterInfoRevealedChanged()
        {
            // Vérifier tous les handlers pour voir si un rôle a été révélé
            CheckForRevealedRoles();
        }

        private void CheckForRevealedRoles()
        {
            foreach (InfoTablePlayerRoleHandler _handler in playerHandlers)
            {
                if (_handler.IsLocked())
                {
                    continue; // Déjà verrouillé
                }

                // Vérifier si le rôle du personnage assigné est révélé
                if (_handler.GetCharacter() != null)
                {
                    Character _character = _handler.GetCharacter();
                    CharacterInfoReveal _info = gameInfoRevealer.GetCharacterInfo(_character.ownerClientId.Value);
                    if ((int)_info.isRoleRevealed > 0)
                    {
                        _handler.LockWithRevealedRole();
                    }
                }
            }
        }

        private void BuildGameUi()
        {
            
            // Reset lists
            playerHandlers.Clear();
            roleCounts.Clear();

            // Create Headers Row
            HorizontalLayoutGroup _headersRow = Instantiate(rowPrefab, contentRoot);
            _headersRow.gameObject.name = "Headers Row";

            // Create individual Headers
            Header _playerRoleHeader = Instantiate(headerPrefab, _headersRow.transform);
            _playerRoleHeader.headerText.SetText("Joueurs / Rôles");
            
            // Create role headers
            foreach (Character _character in characterManager.GetCharacters(false))
            {
                if (_character.role == null)
                {
                    continue;
                }

                KeyValuePair<Role, int> _keyValuePair = roleCounts.FirstOrDefault(_rc => _rc.Key.IsTheSameRole(_character.role));
                if (_keyValuePair.Key != null)
                {
                    roleCounts[_keyValuePair.Key]++;
                }
                else
                {
                    roleCounts[_character.role] = 1;
                }
            }
            
            foreach (KeyValuePair<Role, int> _roleCount in roleCounts)
            {
                Header _roleHeader = Instantiate(headerPrefab, _headersRow.transform);
                _roleHeader.headerText.SetText($"{_roleCount.Key.roleName}{(_roleCount.Value > 1 ? $" *{_roleCount.Value}" : "")}");
                _roleHeader.gameObject.AddComponent<ChildHeader>().SetVerticalHeader(_playerRoleHeader);
                _roleHeader.layoutElement.preferredWidth = 100;
            }
            
            // Create individual Player Rows
            foreach (Character _character in characterManager.GetCharacters(false).Where(_c => _c.isFake == false))
            {
                HorizontalLayoutGroup _playerRow = Instantiate(rowPrefab, contentRoot);
                _playerRow.gameObject.name = $"Player Row - {_character.GetOwnerPseudo()}";

                // Create Player Name Cell
                Header _playerNameCell = Instantiate(playerHeaderPrefab, _playerRow.transform);
                _playerNameCell.headerText.SetText(_character.GetOwnerPseudo());
                _playerNameCell.gameObject.AddComponent<ChildHeader>().SetHorizontalHeader(_playerRoleHeader);
                _playerNameCell.layoutElement.preferredHeight = 100;

                InfoTablePlayerRoleHandler _playerRoleHandler = _playerNameCell.GetComponent<InfoTablePlayerRoleHandler>();
                playerHandlers.Add(_playerRoleHandler);
                _playerRoleHandler.onConflictChanged += OnAnyConflictChanged;
                _playerRoleHandler.onRoleSelectionChanged += OnAnyRoleSelectionChanged; 
                
                // Assigner le personnage au handler
                _playerRoleHandler.SetCharacter(_character);

                // Create Role Cells
                int _roleIndex = 0;
                foreach (KeyValuePair<Role, int> _roleCount in roleCounts)
                {
                    ChildHeader _roleCell = Instantiate(roleCheckPrefab, _playerRow.transform);
                    
                    Header _header = contentRoot.GetChild(0).GetChild(_roleIndex + 1).GetComponent<Header>();
                    _roleCell.SetHorizontalHeader(_header);
                    _roleCell.SetVerticalHeader(_playerNameCell);
                    InfoRoleChecker _infoRoleChecker = _roleCell.GetComponent<InfoRoleChecker>();
                    _infoRoleChecker.Setup(_roleCount.Key);

                    _playerRoleHandler.AddRoleChecker(_infoRoleChecker);
                    
                    _roleIndex++;
                }
            }
            
            // Vérifier si des rôles sont déjà révélés
            CheckForRevealedRoles();
        }
        
        private void Clean()
        {
            // Unsubscribe from events
            foreach (InfoTablePlayerRoleHandler _handler in playerHandlers)
            {
                if (_handler != null)
                {
                    _handler.onConflictChanged -= OnAnyConflictChanged;
                    _handler.onRoleSelectionChanged -= OnAnyRoleSelectionChanged;
                }
            }
            
            // Se désabonner de l'événement de révélation
            if (gameInfoRevealer != null)
            {
                gameInfoRevealer.onCharacterInfoRevealedChanged -= OnCharacterInfoRevealedChanged;
            }
            
            playerHandlers.Clear();
            roleCounts.Clear();
            
            for (int _i = contentRoot.childCount - 1; _i >= 0; _i--)
            {
                Destroy(contentRoot.GetChild(_i).gameObject);
            }
        }

        private void OnAnyConflictChanged()
        {
            CheckGlobalConflicts();
        }

        private void OnAnyRoleSelectionChanged()
        {
            CheckGlobalConflicts();
        }

        private void CheckGlobalConflicts()
        {
            // First, reset all global conflicts (but keep local conflicts)
            foreach (InfoTablePlayerRoleHandler _handler in playerHandlers)
            {
                if (_handler.GetCurrentConflict() == ConflictType.RoleOverCapacity)
                {
                    _handler.CheckLocalConflicts(); // Reset to local conflict state
                }
            }

            // Count "Sure" selections per role
            Dictionary<Role, List<InfoTablePlayerRoleHandler>> _sureCountPerRole = new();
            
            foreach (InfoTablePlayerRoleHandler _handler in playerHandlers)
            {
                List<InfoRoleChecker> _checkers = _handler.GetRoleCheckers();
                
                foreach (InfoRoleChecker _checker in _checkers)
                {
                    if (_checker.GetCurrentCheckerType() == CheckerType.Sure)
                    {
                        Role _role = _checker.role;
                        
                        if (!_sureCountPerRole.ContainsKey(_role))
                        {
                            _sureCountPerRole[_role] = new List<InfoTablePlayerRoleHandler>();
                        }
                        
                        _sureCountPerRole[_role].Add(_handler);
                    }
                }
            }

            // Check for over-capacity conflicts
            foreach (KeyValuePair<Role, List<InfoTablePlayerRoleHandler>> _pair in _sureCountPerRole)
            {
                Role _role = _pair.Key;
                List<InfoTablePlayerRoleHandler> _handlersWithSure = _pair.Value;
                
                // Find the max count for this role
                KeyValuePair<Role, int> _roleCountPair = roleCounts.FirstOrDefault(_rc => _rc.Key.IsTheSameRole(_role));
                int _maxCount = _roleCountPair.Key != null ? _roleCountPair.Value : 0;
                
                if (_handlersWithSure.Count > _maxCount)
                {
                    // Mark all handlers with this "Sure" role as in conflict (unless they already have a local conflict)
                    foreach (InfoTablePlayerRoleHandler _handler in _handlersWithSure)
                    {
                        // Don't mark locked handlers as in conflict
                        if (_handler.IsLocked())
                        {
                            continue;
                        }
                        
                        if (_handler.GetCurrentConflict() != ConflictType.PlayerMultipleRoles)
                        {
                            _handler.SetConflict(ConflictType.RoleOverCapacity);
                        }
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (gameManager != null)
            {
                gameManager.onGameStarted -= OnGameStarted;
            }
            
            foreach (InfoTablePlayerRoleHandler _handler in playerHandlers)
            {
                if (_handler != null)
                {
                    _handler.onConflictChanged -= OnAnyConflictChanged;
                    _handler.onRoleSelectionChanged -= OnAnyRoleSelectionChanged;
                }
            }
        }
    }
}
