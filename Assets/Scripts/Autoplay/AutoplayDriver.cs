#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
using UnityEngine.Rendering;

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
        public bool captureOnStateEnter = true;
        public float stateCaptureDelay = 0.6f;
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
    /// Server-side autoplay brain (dev builds / editor only). Plays every controlled identity — the host's own id and
    /// simulated bots (id &gt;= 100) — through the same code paths a human uses: <see cref="Power.CanUse"/> +
    /// <see cref="Power.StartUse"/> (targets answered by <see cref="AutoplaySelectionAutopilot"/>), the vote state
    /// method, sleep, and the Mage's portal click. Records everything in an <see cref="AutoplayJournal"/> and takes
    /// screenshots at state entries and power verdicts.
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
        private bool running;
        private int captureIndex;
        private ulong? possessedId;

        private readonly Dictionary<ulong, BotTurn> turns = new();
        private readonly HashSet<ulong> votedThisState = new();
        private readonly HashSet<ulong> portalTried = new();
        private readonly Dictionary<Power, Action<PowerVerdict>> verdictHandlers = new();
        private readonly Dictionary<string, Func<object>> probes = new();
        private float stateEnterGameTime;
        private float lastPortalClick;
        private bool pickerHandling;
        private int pickerOpenCount;

        public void Begin(NetworkManager _networkManager, IEnumerable<ulong> _controlledIds, IAutoplayPolicy _policy,
            AutoplayOptions _options, AutoplayJournal _journal)
        {
            networkManager = _networkManager;
            characterManager = CompositionRoot.For(_networkManager).CharacterManager;
            controlledIds = new HashSet<ulong>(_controlledIds);
            policy = _policy;
            options = _options ?? new AutoplayOptions();
            Journal = _journal;

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
            if (!running || networkManager == null || !networkManager.IsServer)
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
                OnStateEntered(_gameManager, _state);
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

        private void OnStateEntered(GameManager _gameManager, GameState _state)
        {
            CurrentState = _state;
            CurrentStateRealSince = Time.realtimeSinceStartup;
            stateEnterGameTime = Time.time;
            turns.Clear();
            votedThisState.Clear();
            portalTried.Clear();
            lastPortalClick = float.NegativeInfinity;

            string _name = _state != null ? _state.GetType().Name : "null";
            Journal.Record("state.enter", $"{_gameManager.currentGameStateIndex.Value}:{_name} day={_gameManager.currentDay}");

            if (options.captureOnStateEnter)
            {
                RequestCapture($"day{_gameManager.currentDay}-{_name}", options.stateCaptureDelay);
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
                // Same server method VoteState.OnPlayerVoted reaches, with the bot's id as the sender.
                _gameManager.DoStateMethodRpc(typeof(VoteState).FullName, "OnPlayerVotedRpc",
                    new[] { new NetworkSerializableObject(_id), new NetworkSerializableObject(_targetId) },
                    new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
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
            float _maxDelay = 0f;
            foreach (float _delay in options.pickerCaptureDelays)
            {
                RequestCapture($"picker{_opening:000}-open-{_delay.ToString("0.00", CultureInfo.InvariantCulture)}s", _delay);
                _maxDelay = Mathf.Max(_maxDelay, _delay);
            }
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
            RequestCapture($"picker{_opening:000}-hover-{Sanitize(DescribeCard(_card))}", options.pickerHoverDwell * 0.8f);
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

            foreach (float _delay in options.verdictCaptureDelays)
            {
                RequestCapture($"verdict-{_power.powerName}-{_verdict}-{_delay.ToString("0.00", CultureInfo.InvariantCulture)}s", _delay);
            }
        }

        /// <summary>Screenshot of what the host screen shows, <paramref name="_delayGameSeconds"/> from now.</summary>
        public void RequestCapture(string _label, float _delayGameSeconds = 0f)
        {
            if (running)
            {
                StartCoroutine(CaptureRoutine(_label, _delayGameSeconds));
            }
        }

        /// <summary>
        /// Registers a named value exported with every capture's state file (e.g. a power's private NV, a UI flag a
        /// scenario wants to assert on). The reader runs at capture time; an exception is exported as its message.
        /// </summary>
        public void AddProbe(string _name, Func<object> _read) => probes[_name] = _read;

        private IEnumerator CaptureRoutine(string _label, float _delayGameSeconds)
        {
            if (_delayGameSeconds > 0f)
            {
                yield return new WaitForSeconds(_delayGameSeconds);
            }

            if (!running)
            {
                yield break;
            }

            // The state file is written in every mode — it is the variable export, PNG or not.
            string _baseName = $"{++captureIndex:000}-{Sanitize(_label)}";
            WriteStateSnapshot(_baseName, _label);

            // WaitForEndOfFrame never resumes in batchmode (no player loop render), and there is nothing to grab
            // without a graphics device: screenshots need a windowed run (editor GUI or a dev build).
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Journal.Record("capture.state", $"{_baseName}.json (no screenshot: batchmode/no graphics)");
                yield break;
            }

            yield return new WaitForEndOfFrame();
            if (!running)
            {
                yield break;
            }

            Texture2D _texture = null;
            try
            {
                _texture = ScreenCapture.CaptureScreenshotAsTexture();
                string _file = $"{_baseName}.png";
                File.WriteAllBytes(Path.Combine(Journal.OutputDirectory, _file), _texture.EncodeToPNG());
                Journal.Record("capture", _file);
            }
            catch (Exception _exception)
            {
                Journal.Record("capture.error", $"{_label}: {_exception.Message}");
            }
            finally
            {
                if (_texture)
                {
                    Destroy(_texture);
                }
            }
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
        private sealed class ProbeValue
        {
            public string name;
            public string value;
        }

        [Serializable]
        private sealed class StateSnapshot
        {
            public string label;
            public float realTime;
            public float gameTime;
            public int frame;
            public string state;
            public int day;
            public string viewAs;
            public float timeScale;
            public bool pickerActive;
            public string pickerKind;
            public int pickableCount;
            public int liftedCount;
            public float frostAlpha;
            public string[] pickableCards;
            public CharacterState[] characters;
            public ProbeValue[] probes;
        }

        private void WriteStateSnapshot(string _baseName, string _label)
        {
            try
            {
                GameManager _gameManager = CompositionRoot.For(networkManager).GameManager;
                var _snapshot = new StateSnapshot
                {
                    label = _label,
                    realTime = Time.realtimeSinceStartup,
                    gameTime = Time.time,
                    frame = Time.frameCount,
                    state = CurrentState != null ? CurrentState.GetType().Name : "null",
                    day = _gameManager != null ? _gameManager.currentDay : -1,
                    viewAs = possessedId.HasValue ? possessedId.Value.ToString() : "host",
                    timeScale = Time.timeScale,
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
                    probes = probes.Select(_probe => new ProbeValue { name = _probe.Key, value = ReadProbe(_probe.Value) }).ToArray(),
                };

                File.WriteAllText(Path.Combine(Journal.OutputDirectory, $"{_baseName}.json"), JsonUtility.ToJson(_snapshot, true));
            }
            catch (Exception _exception)
            {
                Journal.Record("capture.state.error", $"{_label}: {_exception.Message}");
            }
        }

        private static string ReadProbe(Func<object> _read)
        {
            try
            {
                return _read()?.ToString() ?? "null";
            }
            catch (Exception _exception)
            {
                return $"<{_exception.GetType().Name}: {_exception.Message}>";
            }
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

        private static string Sanitize(string _label)
            => new string(_label.Select(_ch => char.IsLetterOrDigit(_ch) || _ch == '-' || _ch == '.' ? _ch : '_').ToArray());
    }
}
#endif
