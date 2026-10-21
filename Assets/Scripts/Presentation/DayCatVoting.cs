using System.Collections.Generic;
using Avatars;
using Board;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Presentation
{
    /// <summary>
    /// T23, the vote in first person (mockups jour-pov v1 H2, v2 A2 + M2 + M3, choices in memory "T23"): by day the player
    /// votes by clicking a cat (the seated reticle); the cards on screen are only to look at.
    ///  • Pointing at a cat (or at its card) swells its light and makes its card stand out (H2).
    ///  • Once the local player has voted, their voted player's card stays raised (M2) and the cat keeps an outline
    ///    (M3), seen by them alone. Who has voted shows on everyone's card ("A voté", A2, the cards' vote panel).
    /// Local presentation; the vote itself goes through <see cref="VoteState.OnPlayerVoted"/> (the server decides).
    /// </summary>
    public class DayCatVoting : MonoBehaviour
    {
        private const float ScanSeconds = 0.25f;

        // Lane A: scene-wired, narrowed to the slices read here.
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private CharacterManager _characterManager;
        [SerializeField] private BoardManager _boardManager;
        [SerializeField] private CameraModeChannel _cameraMode;
        [SerializeField] private DayScreenRows _rows;
        [SerializeField] private CatSpotlights _spotlights;

        private IGameStateQuery Query => _gameManager;
        private ICharacterQuery CharacterQuery => _characterManager;

        private readonly List<CatPointerTarget> _targets = new();
        private CatPointerTarget _pointed;
        private ulong? _cardPointedSeat;
        private CatPointerTarget _outlined;
        private float _nextScan;
        private bool _interactable;

        private void Awake()
        {
            Assert.IsNotNull(_gameManager, "DayCatVoting._gameManager is not wired — wire the GameManager in GameScene.");
            Assert.IsNotNull(_characterManager, "DayCatVoting._characterManager is not wired — wire the CharacterManager in GameScene.");
            Assert.IsNotNull(_boardManager, "DayCatVoting._boardManager is not wired — wire the BoardManager in GameScene.");
            Assert.IsNotNull(_cameraMode, "DayCatVoting._cameraMode is not wired — wire the CameraModeChannel asset.");
            Assert.IsNotNull(_rows, "DayCatVoting._rows is not wired — wire the DayScreenRows in GameScene.");
        }

        private void OnEnable()
        {
            if (_boardManager != null)
            {
                _boardManager.onCardHovered += OnCardHovered;
                _boardManager.onCardUnhovered += OnCardUnhovered;
            }
        }

        private void OnDisable()
        {
            if (_boardManager != null)
            {
                _boardManager.onCardHovered -= OnCardHovered;
                _boardManager.onCardUnhovered -= OnCardUnhovered;
            }
        }

        private void OnDestroy()
        {
            foreach (CatPointerTarget _target in _targets)
            {
                if (_target != null)
                {
                    Unsubscribe(_target);
                }
            }
            _targets.Clear();
        }

        // T23: the board's "Passer le vote" button lay on the table; by day it is a key (Poyo, 2026-10-21).
        [Tooltip("Key that skips the vote by day (a vote for nobody), shown by the key hints.")]
        [SerializeField] private UnityEngine.InputSystem.Key _skipKey = UnityEngine.InputSystem.Key.P;

        public UnityEngine.InputSystem.Key SkipKey => _skipKey;

        /// <summary>Whether the skip key works right now: by day, during the vote, the local player not voted yet.</summary>
        public bool SkipReachable
        {
            get
            {
                if (_cameraMode.Current != CameraMode.Embodied) return false;
                VoteState _vote = Query.GetGameState(Query.currentGameStateIndex.Value) as VoteState;
                Character _local = CharacterQuery.GetLocalCharacter(false);
                return _vote != null && _local != null && !_local.isEliminated.Value && !_local.isFake
                       && !_vote.HasVoted(_local.ownerClientId.Value);
            }
        }

        private void ReadSkipKey()
        {
            UnityEngine.InputSystem.Keyboard _keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (_keyboard == null || !_keyboard[_skipKey].wasPressedThisFrame || DayScreenRows.IsTyping() || !SkipReachable)
            {
                return;
            }
            Diagnostics.Logs.GameLog.Info("vote", "vote.skip.key");
            ((VoteState)Query.GetGameState(Query.currentGameStateIndex.Value)).OnPlayerVoted(VoteState.SKIP_VOTE_ID);
        }

        private void Update()
        {
            ReadSkipKey();
            bool _day = _cameraMode.Current == CameraMode.Embodied;
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanSeconds;
                ScanAvatars();
                UpdateMyVote(_day);
            }
            if (_day != _interactable)
            {
                _interactable = _day;
                foreach (CatPointerTarget _target in _targets)
                {
                    if (_target != null) _target.SetInteractable(_day && !(_target.Avatar != null && _target.Avatar.IsOwner));
                }
                if (!_day)
                {
                    _pointed = null;
                    _cardPointedSeat = null;
                    ShowPointed();
                }
            }
        }

        // ---- the cats

        private void ScanAvatars()
        {
            _targets.RemoveAll(_target => _target == null);
            AvatarManager _manager = AvatarManager.For(NetworkManager.Singleton);
            if (_manager == null)
            {
                return;
            }
            foreach (PlayerAvatar _avatar in _manager.GetAvatars())
            {
                CatPointerTarget _target = _avatar != null ? _avatar.GetComponentInChildren<CatPointerTarget>(true) : null;
                if (_target == null || _targets.Contains(_target))
                {
                    continue;
                }
                _targets.Add(_target);
                _target.Entered += OnCatEntered;
                _target.Exited += OnCatExited;
                _target.Clicked += OnCatClicked;
                _target.SetInteractable(_interactable && !_avatar.IsOwner);
            }
        }

        private void Unsubscribe(CatPointerTarget _target)
        {
            _target.Entered -= OnCatEntered;
            _target.Exited -= OnCatExited;
            _target.Clicked -= OnCatClicked;
        }

        private void OnCatEntered(CatPointerTarget _target)
        {
            if (!_interactable || _target.Avatar == null || _target.Avatar.IsOwner)
            {
                return;
            }
            _pointed = _target;
            ShowPointed();
        }

        private void OnCatExited(CatPointerTarget _target)
        {
            if (_pointed == _target)
            {
                _pointed = null;
                ShowPointed();
            }
        }

        private void OnCatClicked(CatPointerTarget _target)
        {
            if (!_interactable || _target.Avatar == null)
            {
                return;
            }
            VoteState _vote = Query.GetGameState(Query.currentGameStateIndex.Value) as VoteState;
            if (_vote == null)
            {
                return; // the recap: the votes are in
            }
            ulong _me = CharacterQuery.GetLocalClientId();
            ulong _seat = _target.Avatar.ownerClientId.Value;
            if (_seat == _me || _vote.HasVoted(_me))
            {
                return; // a vote is final
            }
            // Same rule as the cards' vote buttons: a chained or eliminated player cannot be voted for.
            Character _character = CharacterQuery.GetCharacter(_seat, false);
            if (_character == null || _character.isFake || _character.isEliminated.Value || _character.isChained.Value)
            {
                return;
            }
            Diagnostics.Logs.GameLog.Info("vote", "vote.cat.click", "to", _seat);
            _vote.OnPlayerVoted(_seat);
        }

        // ---- the cards: pointing at a card points at its cat too (a free cursor reaches them)

        private void OnCardHovered(Card _card)
        {
            if (_interactable && _card != null && _card.visualComponents.inScreenRow)
            {
                _cardPointedSeat = DayScreenRows.SeatOf(_card);
                ShowPointed();
            }
        }

        private void OnCardUnhovered(Card _card)
        {
            if (_cardPointedSeat != null && DayScreenRows.SeatOf(_card) == _cardPointedSeat)
            {
                _cardPointedSeat = null;
                ShowPointed();
            }
        }

        // H2: the pointed cat's light swells, its card stands out in the row.
        private void ShowPointed()
        {
            ulong? _seat = _pointed != null && _pointed.Avatar != null ? _pointed.Avatar.ownerClientId.Value : _cardPointedSeat;
            if (_spotlights != null)
            {
                _spotlights.Boosted = _seat != null ? AvatarOf(_seat.Value) : null;
            }
            _rows.SetLitSeat(_seat);
        }

        // ---- my vote, for me alone (M2 + M3)

        private void UpdateMyVote(bool _day)
        {
            ulong? _votedFor = null;
            if (_day)
            {
                VoteState _vote = DayVote();
                ulong? _target = _vote != null ? _vote.VotedFor(CharacterQuery.GetLocalClientId()) : null;
                if (_target != null && _target.Value != VoteState.SKIP_VOTE_ID)
                {
                    _votedFor = _target;
                }
            }
            _rows.SetRaisedSeat(_votedFor);
            CatPointerTarget _cat = _votedFor != null ? TargetOf(_votedFor.Value) : null;
            if (_cat != _outlined)
            {
                if (_outlined != null) _outlined.SetOutlined(false);
                _outlined = _cat;
                if (_outlined != null) _outlined.SetOutlined(true);
            }
        }

        // The vote whose votes stand: during the vote and its recap.
        private VoteState DayVote()
        {
            GameState _state = Query.GetGameState(Query.currentGameStateIndex.Value);
            if (_state is VoteState _vote)
            {
                return _vote;
            }
            if (_state is VoteRecapState)
            {
                foreach (GameState _candidate in Query.GetGameStates(typeof(VoteState)))
                {
                    if (_candidate is VoteState _found) return _found;
                }
            }
            return null;
        }

        private CatPointerTarget TargetOf(ulong _seat)
        {
            foreach (CatPointerTarget _target in _targets)
            {
                if (_target != null && _target.Avatar != null && _target.Avatar.ownerClientId.Value == _seat)
                {
                    return _target;
                }
            }
            return null;
        }

        private PlayerAvatar AvatarOf(ulong _seat)
        {
            CatPointerTarget _target = TargetOf(_seat);
            return _target != null ? _target.Avatar : null;
        }
    }
}
