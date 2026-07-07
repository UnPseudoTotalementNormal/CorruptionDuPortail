#region

using System;
using System.Collections.Generic;
using System.Linq;
using AYellowpaper.SerializedCollections;
using Cysharp.Threading.Tasks;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

namespace Board.UI.CharacterBar
{
    public class CharactersBar : NetworkBehaviour
    {
        public Transform charactersBarParent;
        
        [SerializeField] private GameObject characterBarObjectPrefab;
        [SerializeField] private GameObject factionGroupPrefab;

        // Story 7.4 lane A: scene-wired CharacterManager, replacing the GameManager hub-hop.
        [SerializeField] private CharacterManager characterManager;
        // Story 9.1 (Epic 9 / D3): read slice of the scene-wired characterManager (D-NFR6 internal-narrowing).
        private ICharacterQuery CharacterQuery => characterManager;

        // Story 8.2 lane A: scene-wired GameManager narrowed to the read slice for the AwakeningState
        // lookup. Stays null-tolerant (GetAwakeningState already guarded the locator) — guard #2 verifies
        // the scene wiring, runtime tolerates a missing manager in bare harnesses.
        [SerializeField] private GameManager gameManager;
        private IGameStateQuery Query => gameManager;

        [Header("Faction Visuals")]
        [SerializeField] private SerializedDictionary<FactionType, Sprite> factionIcons = new();
        [SerializeField] private SerializedDictionary<FactionType, Color> factionTextColors = new();

        [Header("Spacing Settings")]
        [SerializeField] private float intraGroupSpacing = 5f;
        [SerializeField] private float interGroupSpacing = 40f;
        [SerializeField] private float titleToGroupSpacing = 10f;
        
        public List<CharactersBarObject> charactersBarObjects = new();
        
        public event Action<Character> onCharacterBarClicked;
        public event Action<Character> onCharacterBarHovered;
        public event Action<Character> onCharacterBarUnhovered;

        private void Start()
        {
            Assert.IsNotNull(characterManager, "CharactersBar.characterManager is not wired — wire it in GameScene (the composition root).");
            CharacterQuery.onCharactersListUpdated += OnCharacterListUpdated;
        }

        private void OnCharacterListUpdated(List<Character> _characters)
        {
            foreach (var _character in _characters)
            {
                var _characterBarObject = charactersBarObjects.Find(_obj => _obj.playerCharacter.ownerClientId.Value == _character.ownerClientId.Value);
                if (_characterBarObject)
                {
                    _characterBarObject.SetCharacter(_character);
                }
            }
        }
        
        public List<CharactersBarObject> GetCharacterBarObject(Role _role)
        {
            return charactersBarObjects.FindAll(_obj => _obj.playerCharacter.role.IsTheSameRole(_role));
        }

        private AwakeningState _awakeningState;

        private AwakeningState GetAwakeningState()
        {
            if (_awakeningState == null && gameManager != null)
            {
                _awakeningState = (AwakeningState)Query.GetGameStates(typeof(AwakeningState)).FirstOrDefault();
            }
            return _awakeningState;
        }

        public IEnumerable<Character> SortCharacters(IEnumerable<Character> _characters)
        {
            return SortCharacters(_characters, GetAwakeningState());
        }

        public IEnumerable<Character> SortCharacters(IEnumerable<Character> _characters, AwakeningState _state)
        {
            return _characters
                .OrderBy(_c => _state != null ? _state.GetAwakeningLayerIndex(_c.role) : int.MaxValue)
                .ThenBy(_c => _c.role != null ? (int)_c.role.roleID : int.MaxValue)
                .ThenBy(_c => _c.ownerClientId.Value);
        }

        private static FactionType GetFaction(Character _character)
        {
            return _character != null && _character.role != null ? _character.role.factionType : FactionType.unknown;
        }

        // Groups an already-sorted character sequence into consecutive runs of the same
        // faction. A new group starts every time the faction changes in awakening order,
        // so a faction that awakens at two non-contiguous layers yields two separate groups.
        public List<List<Character>> GroupConsecutiveByFaction(IEnumerable<Character> _sortedCharacters)
        {
            var _groups = new List<List<Character>>();
            List<Character> _currentGroup = null;
            bool _hasFaction = false;
            FactionType _currentFaction = FactionType.unknown;

            foreach (Character _character in _sortedCharacters)
            {
                FactionType _faction = GetFaction(_character);
                if (_currentGroup == null || !_hasFaction || _faction != _currentFaction)
                {
                    _currentGroup = new List<Character>();
                    _groups.Add(_currentGroup);
                    _currentFaction = _faction;
                    _hasFaction = true;
                }
                _currentGroup.Add(_character);
            }

            return _groups;
        }

        public void ResetCharactersBar(List<Character> _characters)
        {
            // Re-fetch the awakening state on every rebuild so a new game session
            // (rematch) cannot keep a stale reference to a previous state instance.
            _awakeningState = null;

            // Set inter-group spacing on the main parent layout
            if (charactersBarParent.TryGetComponent<UnityEngine.UI.HorizontalLayoutGroup>(out var _parentHlg))
            {
                _parentHlg.spacing = interGroupSpacing;
            }

            for (int i = 0; i < charactersBarParent.childCount; i++)
            {
                Destroy(charactersBarParent.GetChild(i).gameObject);
            }

            charactersBarObjects.Clear();
            
            var _sortedCharacters = SortCharacters(_characters).ToList();
            var _groups = GroupConsecutiveByFaction(_sortedCharacters);

            foreach (var _group in _groups)
            {
                FactionType _faction = GetFaction(_group[0]);
                Transform _currentGroupContainer = null;

                if (factionGroupPrefab != null)
                {
                    GameObject _groupObj = Instantiate(factionGroupPrefab, charactersBarParent);
                    
                    // Set vertical spacing between title and icons
                    if (_groupObj.TryGetComponent<UnityEngine.UI.VerticalLayoutGroup>(out var _groupVlg))
                    {
                        _groupVlg.spacing = titleToGroupSpacing;
                    }

                    // Set faction title
                    var _tmp = _groupObj.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                    if (_tmp != null)
                    {
                        _tmp.text = _faction.ToString().ToUpper();

                        // Tint the faction title per faction (e.g. green for chosen, orange
                        // for marginal). Falls back to the prefab's color when unmapped.
                        if (factionTextColors.TryGetValue(_faction, out var _factionColor))
                        {
                            _tmp.color = _factionColor;
                        }
                    }

                    // Set the dynamic faction icon (hidden when no sprite is mapped for this faction)
                    var _iconTransform = _groupObj.transform.Find("FactionTitleWrapper/FactionIcon");
                    if (_iconTransform != null && _iconTransform.TryGetComponent<UnityEngine.UI.Image>(out var _iconImage))
                    {
                        if (factionIcons.TryGetValue(_faction, out var _factionSprite) && _factionSprite != null)
                        {
                            _iconImage.sprite = _factionSprite;
                            _iconImage.enabled = true;
                        }
                        else
                        {
                            _iconImage.enabled = false;
                        }
                    }

                    // Find the container for icons and set intra-group spacing
                    _currentGroupContainer = _groupObj.transform.Find("CharactersContainer");
                    if (_currentGroupContainer != null)
                    {
                        if (_currentGroupContainer.TryGetComponent<UnityEngine.UI.HorizontalLayoutGroup>(out var _groupHlg))
                        {
                            _groupHlg.spacing = intraGroupSpacing;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[CharactersBar] '{factionGroupPrefab.name}' has no 'CharactersContainer' child; " +
                                         "character icons will be parented to the group root instead.", _groupObj);
                        _currentGroupContainer = _groupObj.transform; // Fallback
                    }
                }
                else
                {
                    Debug.LogWarning("[CharactersBar] factionGroupPrefab is not assigned; " +
                                     "characters will be shown flat without faction grouping.", this);
                    _currentGroupContainer = charactersBarParent;
                }

                foreach (Character _character in _group)
                {
                    GameObject _characterBarChild = Instantiate(characterBarObjectPrefab, _currentGroupContainer);

                    var _characterBarObject = _characterBarChild.GetComponent<CharactersBarObject>();
                    _characterBarObject.SetCharacter(_character);
                    _characterBarObject.onCharacterBarObjectClicked += (_characterClicked) =>
                    {
                        onCharacterBarClicked?.Invoke(_characterClicked);
                    };
                    _characterBarObject.onCharacterBarObjectHovered += (_characterHovered) =>
                    {
                        onCharacterBarHovered?.Invoke(_characterHovered);
                    };
                    _characterBarObject.onCharacterBarObjectUnhovered += (_characterUnhovered) =>
                    {
                        onCharacterBarUnhovered?.Invoke(_characterUnhovered);
                    };
                    
                    charactersBarObjects.Add(_characterBarObject);
                }
            }

            // The bar is populated once at game intro (GameIntroductionState) into nested
            // HorizontalLayoutGroups on a world-space canvas, where each leaf carries its own
            // nested Canvas (a layout-rebuild boundary). On some frames Unity's automatic layout
            // pass is missed/mis-registered and every RectTransform stays at its origin, so the
            // whole bar renders as one overlapping pile at the centre. Force the rebuild explicitly.
            // A deferred second pass covers the case where the subtree is (re)activated the same frame.
            RebuildLayout();
            RebuildLayoutDeferred().Forget();
        }

        private void RebuildLayout()
        {
            if (charactersBarParent is RectTransform _parentRect)
            {
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRect);
            }
        }

        private async UniTaskVoid RebuildLayoutDeferred()
        {
            await UniTask.NextFrame();
            // The bar may have been destroyed/rebuilt in the meantime (rematch, lobby return).
            if (this == null || charactersBarParent == null) return;
            RebuildLayout();
        }

        public void DestroyCharactersBar()
        {
            for (int i = 0; i < charactersBarParent.childCount; i++)
            {
                Destroy(charactersBarParent.GetChild(i).gameObject);
            }
            
            charactersBarObjects.Clear();
        }
    }
}
