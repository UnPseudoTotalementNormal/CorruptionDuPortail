#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Characters;
using Characters.Powers;
using CorruptionDuPortail.Domain.Powers;
using GameLogic;
using GameLogic.GameStates;
using Network;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UI.BoardUI;
using UnityEngine;
using UnityEngine.EventSystems;
using Board;
using Unpseudo.Autoplay;

namespace Autoplay
{
    [Serializable]
    public sealed class AutoplayOptions
    {
        [Tooltip("Game seconds a bot waits before acting (after waking, between two powers, at vote start).")]
        public float thinkDelay = 1.5f;
        [Tooltip("Game seconds before a power still in use is cancelled.")]
        public float powerTimeout = 15f;
        public double powerUseProbability = 1.0;
        public double voteProbability = 1.0;
        [Tooltip("Possess the acting bot's identity so the host screen shows what that player sees (local-only feedback).")]
        public bool possessActor = true;
        public bool captureOnVerdict = true;
        [Tooltip("Game seconds after a power verdict at which a screenshot is taken (a burst catches the feedback animation).")]
        public float[] verdictCaptureDelays = { 0.15f, 0.5f, 1.2f };

        [Tooltip("Let the REAL card picker show (blur veil, lifted cards) instead of answering selections invisibly; " +
                 "each opening is captured in a burst, then a valid card is hovered and clicked through its pointer handlers.")]
        public bool visualPicker;
        [Tooltip("Game seconds after a picker opens at which a capture is taken.")]
        public float[] pickerCaptureDelays = { 0f, 0.1f, 0.25f, 0.5f, 1f };
        [Tooltip("Game seconds the chosen card stays hovered (captured) before the click.")]
        public float pickerHoverDwell = 0.6f;
    }

    /// <summary>
    /// Autoplay brain (dev builds / editor only). On the host it plays the host's own id and the simulated bots
    /// (id &gt;= 100); on a real network client it plays that client's own seat only — both through the same code
    /// paths a human uses: <see cref="Power.CanUse"/> +
    /// <see cref="Power.StartUse"/> (targets answered by <see cref="AutoplaySelectionAutopilot"/>), the vote state
    /// method, sleep, and the Mage's portal click. Records its decisions in the run's <see cref="AutoplayJournal"/> and
    /// requests captures at power verdicts and picker openings; phase tracking / phase captures are the runner's job.
    /// </summary>
    public sealed class AutoplayDriver : MonoBehaviour
    {
        private sealed class BotTurn
        {
            public float idleSince;
            public Power active;
            public float activeSince;
            public bool done;
            public readonly HashSet<Power> tried = new();
        }

        public AutoplayJournal Journal { get; private set; }
        public GameState CurrentState { get; private set; }
        public float CurrentStateRealSince { get; private set; }

        private NetworkManager networkManager;
        private CharacterManager characterManager;
        private HashSet<ulong> controlledIds;
        private IAutoplayPolicy policy;
        private AutoplayOptions options;
        private AutoplaySelectionAutopilot autopilot;
        private AutoplayCapture capture;
        private bool running;
        private ulong? possessedId;

        private readonly Dictionary<ulong, BotTurn> turns = new();
        private readonly HashSet<ulong> votedThisState = new();
        private readonly HashSet<ulong> portalTried = new();
        private readonly Dictionary<Power, Action<PowerVerdict>> verdictHandlers = new();
        private float stateEnterGameTime;
        private float lastPortalClick;
        private bool pickerHandling;
        private int pickerOpenCount;

        public void Begin(NetworkManager _networkManager, IEnumerable<ulong> _controlledIds, IAutoplayPolicy _policy,
            AutoplayOptions _options, AutoplayJournal _journal, AutoplayCapture _capture)
        {
            networkManager = _networkManager;
            characterManager = CompositionRoot.For(_networkManager).CharacterManager;
            controlledIds = new HashSet<ulong>(_controlledIds);
            policy = _policy;
            options = _options ?? new AutoplayOptions();
            Journal = _journal;
            capture = _capture;

            autopilot = new AutoplaySelectionAutopilot(characterManager, policy, Journal);
            if (!options.visualPicker)
            {
                // Visual runs keep the real picker on screen; the driver clicks its cards instead (UpdateVisualPicker).
                SelectionFlowService.instance.Autopilot = autopilot;
            }
            running = true;

            Journal.Record("autoplay.begin", $"controlled=[{string.Join(",", controlledIds.OrderBy(_id => _id))}]");
        }

        public void End()
        {
            if (!running)
            {
                return;
            }

            running = false;
            if (SelectionFlowService.instance.Autopilot == autopilot)
            {
                SelectionFlowService.instance.Autopilot = null;
            }

            foreach (KeyValuePair<Power, Action<PowerVerdict>> _handler in verdictHandlers)
            {
                if (_handler.Key)
                {
                    _handler.Key.onPowerVerdict -= _handler.Value;
                }
            }
            verdictHandlers.Clear();

            if (possessedId.HasValue && characterManager)
            {
                characterManager.SetPossessedIdentity(null);
            }
            possessedId = null;
        }

        private void OnDestroy() => End();

        /// <summary>One line per real player: id, role, faction and final flags.</summary>
        public IEnumerable<string> DescribeRoster()
        {
            if (!characterManager)
            {
                return Array.Empty<string>();
            }

            return characterManager.GetCharacters(false)
                .Where(_c => _c && !_c.isFake)
                .OrderBy(_c => _c.ownerClientId.Value)
                .Select(_c => $"{_c.ownerClientId.Value} {_c.role?.roleName} faction={_c.role?.factionType} " +
                              $"chained={_c.isChained.Value} corrupted={_c.isCorrupted.Value} healed={_c.isHealed.Value}")
                .ToList();
        }

        private void Update()
        {
            if (!running || networkManager == null || !(networkManager.IsServer || networkManager.IsConnectedClient))
            {
                return;
            }

            autopilot.Pump();
            if (options.visualPicker)
            {
                UpdateVisualPicker();
            }

            GameManager _gameManager = CompositionRoot.For(networkManager).GameManager;
            if (_gameManager == null || !_gameManager.IsSpawned)
            {
                return;
            }

            GameState _state = _gameManager.GetGameState(_gameManager.currentGameStateIndex.Value);
            if (_state != CurrentState)
            {
                OnStateEntered(_state);
            }

            switch (_state)
            {
                case AwakeningState _:
                    UpdateAwakening();
                    break;
                case VoteState _:
                    UpdateVote(_gameManager);
                    break;
                case TakeDownThePortalState _portal:
                    UpdatePortal(_gameManager, _portal);
                    break;
            }
        }

        // Per-phase bookkeeping only: the runner records the phase change and captures it.
        private void OnStateEntered(GameState _state)
        {
            CurrentState = _state;
            CurrentStateRealSince = Time.realtimeSinceStartup;
            stateEnterGameTime = Time.time;
            turns.Clear();
            votedThisState.Clear();
            portalTried.Clear();
            lastPortalClick = float.NegativeInfinity;
            StartCoroutine(RecordStateHash(_state));
        }

        // Desync detector: once the phase has settled, every process (host and each real client) journals a hash of
        // the same canonical view of the replicated state. tools compare them phase by phase across processes.
        private IEnumerator RecordStateHash(GameState _state)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            if (!running || _state != CurrentState)
            {
                yield break;
            }

            GameManager _gameManager = CompositionRoot.For(networkManager).GameManager;
            if (_gameManager == null)
            {
                yield break;
            }

            string _canonical = string.Join(";", characterManager.GetCharacters(false)
                .Where(_c => _c && !_c.isFake)
                .OrderBy(_c => _c.ownerClientId.Value)
                .Select(_c => $"{_c.ownerClientId.Value}:{_c.role?.roleName}:{(_c.isChained.Value ? 1 : 0)}{(_c.isCorrupted.Value ? 1 : 0)}" +
                              $"{(_c.isHealed.Value ? 1 : 0)}{(_c.isBlessed.Value ? 1 : 0)}{(_c.isEliminated.Value ? 1 : 0)}"));
            string _phase = $"{_gameManager.currentGameStateIndex.Value}:{_state.GetType().Name} day={_gameManager.currentDay}";
            Journal.Record("state.hash", $"{_phase} | {StableHash(_canonical)} | {_canonical}");
        }

        private static string StableHash(string _text)
        {
            unchecked
            {
                ulong _hash = 14695981039346656037UL; // FNV-1a 64
                foreach (char _ch in _text)
                {
                    _hash = (_hash ^ _ch) * 1099511628211UL;
                }
                return _hash.ToString("x16");
            }
        }

        private void UpdateAwakening()
        {
            bool _someoneActing = turns.Values.Any(_t => _t.active != null);

            foreach (Character _character in characterManager.GetCharacters(false))
            {
                if (!_character || _character.isFake)
                {
                    continue;
                }

                ulong _id = _character.ownerClientId.Value;
                if (!controlledIds.Contains(_id))
                {
                    continue;
                }

                if (!_character.isAwakened.Value)
                {
                    turns.Remove(_id);
                    continue;
                }

                if (!turns.TryGetValue(_id, out BotTurn _turn))
                {
                    _turn = new BotTurn { idleSince = Time.time };
                    turns[_id] = _turn;
                    Journal.Record("awake", Describe(_character));
                }

                if (_turn.done)
                {
                    continue;
                }

                if (_turn.active != null)
                {
                    if (_turn.active.isCurrentlyUsed)
                    {
                        if (Time.time - _turn.activeSince < options.powerTimeout)
                        {
                            _turn.active.UsingPowerUpdate();
                            continue;
                        }

                        Journal.Record("power.timeout", $"{_id} {_turn.active.powerName}");
                        _turn.active.Cancel();
                    }

                    Journal.Record("power.end", $"{_id} {_turn.active.powerName}");
                    _turn.active = null;
                    _turn.idleSince = Time.time;
                    continue;
                }

                // One power in flight at a time when possessing, so the screen (and its screenshots) shows one actor.
                if (options.possessActor && _someoneActing)
                {
                    continue;
                }

                if (Time.time - _turn.idleSince < options.thinkDelay)
                {
                    continue;
                }

                Power _next = _character.role?.powers.FirstOrDefault(_p =>
                    _p && !_turn.tried.Contains(_p) && !_p.isPassive && SafeCanUse(_p));
                if (_next == null)
                {
                    _turn.done = true;
                    Journal.Record("sleep", $"{_id}");
                    _character.SleepCharacterServerRpc();
                    continue;
                }

                _turn.tried.Add(_next);
                if (!policy.Roll(options.powerUseProbability, "power.use"))
                {
                    Journal.Record("power.skip", $"{_id} {_next.powerName}");
                    continue;
                }

                if (options.possessActor)
                {
                    Possess(_id);
                }

                SubscribeVerdict(_next);
                Journal.Record("power.start", $"{_id} {_character.role.roleName} {_next.powerName}");
                _turn.active = _next;
                _turn.activeSince = Time.time;
                _someoneActing = true;
                try
                {
                    _next.StartUse();
                }
                catch (Exception _exception)
                {
                    Journal.Record("power.error", $"{_id} {_next.powerName} {_exception.GetType().Name}: {_exception.Message}");
                    Debug.LogException(_exception);
                    _turn.active = null;
                    _turn.idleSince = Time.time;
                }
            }
        }

        private void UpdateVote(GameManager _gameManager)
        {
            if (Time.time - stateEnterGameTime < options.thinkDelay)
            {
                return;
            }

            List<Character> _characters = characterManager.GetCharacters(false);
            foreach (Character _voter in _characters)
            {
                if (!_voter || _voter.isFake)
                {
                    continue;
                }

                ulong _id = _voter.ownerClientId.Value;
                if (!controlledIds.Contains(_id) || !votedThisState.Add(_id))
                {
                    continue;
                }

                if (_voter.isEliminated.Value || !policy.Roll(options.voteProbability, "vote"))
                {
                    Journal.Record("vote.skip", $"{_id}");
                    continue;
                }

                List<Character> _targets = _characters
                    .Where(_t => _t && !_t.isFake && _t.ownerClientId.Value != _id && !_t.isChained.Value && !_t.isEliminated.Value)
                    .OrderBy(_t => _t.ownerClientId.Value)
                    .ToList();
                if (_targets.Count == 0)
                {
                    continue;
                }

                ulong _targetId = policy.Choose(_targets, "vote").ownerClientId.Value;
                if (networkManager.IsServer)
                {
                    // Same server method VoteState.OnPlayerVoted reaches, with the bot's id as the sender.
                    _gameManager.DoStateMethodRpc(typeof(VoteState).FullName, "OnPlayerVotedRpc",
                        new[] { new NetworkSerializableObject(_id), new NetworkSerializableObject(_targetId) },
                        new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
                }
                else if (CurrentState is VoteState _voteState)
                {
                    // A real client votes exactly like its vote button does.
                    _voteState.OnPlayerVoted(_targetId);
                }
                Journal.Record("vote", $"{_id} -> {_targetId}");
                return; // one vote per frame
            }
        }

        private void UpdatePortal(GameManager _gameManager, TakeDownThePortalState _portal)
        {
            ulong _mageId = _portal.mageCharacterOwnerId;
            if (!_portal.shouldActivate || !controlledIds.Contains(_mageId))
            {
                return;
            }

            // The state itself waits ~3 s for the cards before it listens for the Mage's click.
            if (Time.time - stateEnterGameTime < 5f || Time.time - lastPortalClick < 3f)
            {
                return;
            }

            List<Character> _candidates = characterManager.GetCharacters(false)
                .Where(_c => _c && !_c.isFake && _c.ownerClientId.Value != _mageId && !portalTried.Contains(_c.ownerClientId.Value))
                .OrderBy(_c => _c.ownerClientId.Value)
                .ToList();
            if (_candidates.Count == 0)
            {
                portalTried.Clear();
                return;
            }

            ulong _targetId = policy.Choose(_candidates, "portal.character").ownerClientId.Value;
            portalTried.Add(_targetId);
            lastPortalClick = Time.time;
            if (options.possessActor)
            {
                Possess(_mageId);
            }

            // The role half of the guess goes through SelectionFlowService, i.e. the autopilot.
            _gameManager.DoStateMethodRpc(typeof(TakeDownThePortalState).FullName, "OnCharacterClickServer",
                new[] { new NetworkSerializableObject(_targetId) },
                new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
            Journal.Record("portal.click", $"{_mageId} -> {_targetId}");
        }

        private void UpdateVisualPicker()
        {
            CardPickerManager _picker = CardPickerManager.instance;
            if (pickerHandling || _picker == null || !_picker.IsPickerActive)
            {
                return;
            }

            pickerHandling = true;
            StartCoroutine(HandlePickerOpening(_picker, ++pickerOpenCount));
        }

        // One picker opening: a capture burst (blur veil, lifted cards…), then a hover + click on a valid card through
        // the card's own pointer handlers — what the EventSystem calls for a mouse. A two-step flow (character then
        // role) reopens the picker, which is handled as the next opening.
        private IEnumerator HandlePickerOpening(CardPickerManager _picker, int _opening)
        {
            Journal.Record("picker.open", $"#{_opening} {DescribePicker(_picker)}");
            capture.RequestBurst($"picker{_opening:000}-open", options.pickerCaptureDelays);
            float _maxDelay = options.pickerCaptureDelays.DefaultIfEmpty(0f).Max();
            yield return new WaitForSeconds(_maxDelay + 0.05f);

            List<Card> _cards = _picker.IsPickerActive
                ? _picker.PickableCards.Where(_c => _c).OrderBy(DescribeCard, StringComparer.Ordinal).ToList()
                : new List<Card>();
            if (_cards.Count == 0)
            {
                Journal.Record("picker.closed", $"#{_opening} closed or empty before the pick");
                pickerHandling = false;
                yield break;
            }

            Card _card = policy.Choose(_cards, "picker.card");
            var _pointer = new PointerEventData(EventSystem.current);
            _card.OnPointerEnter(_pointer);
            Journal.Record("picker.hover", $"#{_opening} {DescribeCard(_card)}");
            capture.Request($"picker{_opening:000}-hover-{DescribeCard(_card)}", options.pickerHoverDwell * 0.8f);
            yield return new WaitForSeconds(options.pickerHoverDwell);

            if (_card && _picker.IsPickerActive)
            {
                _card.OnPointerClick(_pointer);
                _card.OnPointerExit(_pointer);
                Journal.Record("picker.click", $"#{_opening} {DescribeCard(_card)}");
            }

            yield return null;
            pickerHandling = false;
        }

        private static string DescribePicker(CardPickerManager _picker)
            => $"kind={(_picker.IsRolePicker ? "role" : "character")} pickable={_picker.PickableCards.Count} lifted={_picker.LiftedCharacterCards.Count} " +
               $"frost={_picker.FrostAlpha.ToString("0.00", CultureInfo.InvariantCulture)}";

        // Role-picker cards also carry a characterInfo (one representative per role), so the picker's own kind decides.
        private static string DescribeCard(Card _card)
        {
            if (_card == null) return "null";
            bool _rolePicker = CardPickerManager.instance != null && CardPickerManager.instance.IsRolePicker;
            if (_rolePicker || _card.characterInfo == null) return $"role:{_card.roleInfo?.roleName}";
            return $"char:{_card.characterInfo.ownerClientId.Value}";
        }

        private void Possess(ulong _id)
        {
            if (!networkManager.IsServer)
            {
                return; // possession is the host's debug identity switch; a real client is already itself
            }

            ulong? _target = _id == networkManager.LocalClientId ? null : _id;
            if (possessedId == _target)
            {
                return;
            }

            possessedId = _target;
            characterManager.SetPossessedIdentity(_target);
            Journal.Record("possess", _target.HasValue ? _target.Value.ToString() : "host");
        }

        private void SubscribeVerdict(Power _power)
        {
            if (verdictHandlers.ContainsKey(_power))
            {
                return;
            }

            Action<PowerVerdict> _handler = _verdict => OnVerdict(_power, _verdict);
            _power.onPowerVerdict += _handler;
            verdictHandlers[_power] = _handler;
        }

        private void OnVerdict(Power _power, PowerVerdict _verdict)
        {
            Journal.Record("power.verdict", $"{_power.ownerClientId.Value} {_power.powerName} {_verdict}");
            if (!options.captureOnVerdict)
            {
                return;
            }

            capture.RequestBurst($"verdict-{_power.powerName}-{_verdict}", options.verdictCaptureDelays);
        }

        [Serializable]
        private sealed class CharacterState
        {
            public ulong id;
            public string role;
            public string faction;
            public bool awakened;
            public bool chained;
            public bool corrupted;
            public bool healed;
            public bool blessed;
            public bool eliminated;
            public string[] powersInUse;
        }

        [Serializable]
        private sealed class StateSnapshot
        {
            public string state;
            public int day;
            public string viewAs;
            public bool pickerActive;
            public string pickerKind;
            public int pickableCount;
            public int liftedCount;
            public float frostAlpha;
            public string[] pickableCards;
            public CharacterState[] characters;
        }

        /// <summary>Corruption du Portail state exported with every capture (the package adds time, phase, probes).</summary>
        public string ExportStateJson()
        {
            GameManager _gameManager = CompositionRoot.For(networkManager).GameManager;
            var _snapshot = new StateSnapshot
            {
                state = CurrentState != null ? CurrentState.GetType().Name : "null",
                day = _gameManager != null ? _gameManager.currentDay : -1,
                viewAs = possessedId.HasValue ? possessedId.Value.ToString() : "host",
                pickerActive = CardPickerManager.instance != null && CardPickerManager.instance.IsPickerActive,
                pickerKind = CardPickerManager.instance == null || !CardPickerManager.instance.IsPickerActive ? "none"
                    : CardPickerManager.instance.IsRolePicker ? "role" : "character",
                pickableCount = CardPickerManager.instance != null ? CardPickerManager.instance.PickableCards.Count : 0,
                liftedCount = CardPickerManager.instance != null ? CardPickerManager.instance.LiftedCharacterCards.Count : 0,
                frostAlpha = CardPickerManager.instance != null ? CardPickerManager.instance.FrostAlpha : -1f,
                pickableCards = CardPickerManager.instance != null
                    ? CardPickerManager.instance.PickableCards.Where(_c => _c).Select(DescribeCard).ToArray()
                    : Array.Empty<string>(),
                characters = characterManager.GetCharacters(false)
                    .Where(_c => _c && !_c.isFake)
                    .OrderBy(_c => _c.ownerClientId.Value)
                    .Select(_c => new CharacterState
                    {
                        id = _c.ownerClientId.Value,
                        role = _c.role?.roleName.ToString(),
                        faction = _c.role?.factionType.ToString(),
                        awakened = _c.isAwakened.Value,
                        chained = _c.isChained.Value,
                        corrupted = _c.isCorrupted.Value,
                        healed = _c.isHealed.Value,
                        blessed = _c.isBlessed.Value,
                        eliminated = _c.isEliminated.Value,
                        powersInUse = _c.role?.powers.Where(_p => _p && _p.isCurrentlyUsed).Select(_p => _p.powerName.ToString()).ToArray()
                                      ?? Array.Empty<string>(),
                    })
                    .ToArray(),
            };

            return JsonUtility.ToJson(_snapshot);
        }

        private static bool SafeCanUse(Power _power)
        {
            try
            {
                return _power.CanUse();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Describe(Character _character)
            => $"{_character.ownerClientId.Value} {_character.role?.roleName}";

    }
}
#endif
