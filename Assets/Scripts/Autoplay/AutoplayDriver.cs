#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Characters;
using Characters.Powers;
using ChatSystem;
using CorruptionDuPortail.Domain;
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

        [Tooltip("Scenario lever: when set, every bot votes for the living player whose role name contains this text " +
                 "(forces a situation to happen, e.g. chain the Mage to exercise the portal). Empty = random legal votes.")]
        public string voteFocusRole;

        [Tooltip("Scenario lever: target picks prefer a valid candidate matching this (client | host | bot | fake | " +
                 "role-name fragment); random among all valid ones when none matches. Empty = random legal targets.")]
        public string targetFocus;
        [Tooltip("Restricts targetFocus to picks made while a power whose name contains this text is used.")]
        public string targetFocusPower;

        [Tooltip("Scenario lever: every controlled player writes one tokenized line per phase in each private chat " +
                 "channel it belongs to; every process journals what it receives (chat.sent / chat.recv / chat.members).")]
        public bool chat;

        [Tooltip("Test-only speed lever: put fake roles (seats with no player) back to sleep shortly after they wake, " +
                 "instead of the game's frame-random fake skip that often waits for the whole layer timer.")]
        public bool fastFakes;
        [Tooltip("Game seconds a fake role stays awake when fastFakes is on.")]
        public float fakeSleepDelay = 1f;
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
        private int awakeningLayer = -1;
        private float layerStartGame;
        private float layerStartReal;
        private readonly Dictionary<Character, float> fakeAwakeSince = new();
        private bool pickerHandling;
        private int pickerOpenCount;
        private GameInfoRevealer revealer;
        private readonly Dictionary<string, string> knownLevels = new();
        private ChatManager chatManager;
        private int chatSequence;
        private readonly HashSet<string> chatSentThisState = new();
        private readonly Dictionary<int, string> chatMembersRecorded = new();

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

            autopilot = new AutoplaySelectionAutopilot(characterManager, policy, Journal)
            {
                TargetFocus = options.targetFocus,
                TargetFocusPower = options.targetFocusPower,
            };
            if (!options.visualPicker)
            {
                // Visual runs keep the real picker on screen; the driver clicks its cards instead (UpdateVisualPicker).
                SelectionFlowService.instance.Autopilot = autopilot;
            }
            running = true;

            // What each controlled player knows about the others (server ledger slices since NET-10): journaled on
            // every change so a scenario can prove that a reveal reached the right screen (e.g. a client-cast power).
            revealer = CompositionRoot.For(_networkManager).GameInfoRevealer;
            if (revealer != null)
            {
                revealer.onCharacterInfoRevealedChanged += OnKnowledgeChanged;
            }

            // Every chat line this process receives is journaled (chat.recv), lever or not: power feedback such as
            // the Orpheline's contact line arrives through the chat. Writing is the "chat" lever (UpdateChat).
            chatManager = ChatManager.instance;
            if (chatManager != null)
            {
                chatManager.onChatMessageReceived += OnChatReceived;
            }

            Journal.Record("autoplay.begin", $"controlled=[{string.Join(",", controlledIds.OrderBy(_id => _id))}]");
        }

        public void End()
        {
            if (!running)
            {
                return;
            }

            running = false;
            if (revealer != null)
            {
                revealer.onCharacterInfoRevealedChanged -= OnKnowledgeChanged;
            }
            if (chatManager != null)
            {
                chatManager.onChatMessageReceived -= OnChatReceived;
            }
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

            if (options.chat)
            {
                UpdateChat(_gameManager, _state);
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

        // One event per awakening layer (who woke, real seat or fake role) and its duration: where the night's time goes.
        private void TrackAwakeningLayer(AwakeningState _awakening)
        {
            if (_awakening.currentAwakeningIndex == awakeningLayer)
            {
                return;
            }

            EndAwakeningLayer();
            awakeningLayer = _awakening.currentAwakeningIndex;
            layerStartGame = Time.time;
            layerStartReal = Time.realtimeSinceStartup;
            string _who = string.Join(",", _awakening.currentlyAwakenedCharacters.Where(_c => _c)
                .Select(_c => $"{(_c.isFake ? "fake" : _c.ownerClientId.Value.ToString())}:{_c.role?.roleName}"));
            Journal.Record("awake.layer", $"#{awakeningLayer} [{_who}]");
        }

        private void EndAwakeningLayer()
        {
            if (awakeningLayer < 0)
            {
                return;
            }

            Journal.Record("awake.layer.end", string.Format(CultureInfo.InvariantCulture, "#{0} game={1:0.0}s real={2:0.0}s",
                awakeningLayer, Time.time - layerStartGame, Time.realtimeSinceStartup - layerStartReal));
            awakeningLayer = -1;
        }

        private void SleepFakesEarly()
        {
            foreach (Character _fake in characterManager.GetCharacters(false))
            {
                if (!_fake || !_fake.isFake)
                {
                    continue;
                }

                if (!_fake.isAwakened.Value)
                {
                    fakeAwakeSince.Remove(_fake);
                    continue;
                }

                if (!fakeAwakeSince.TryGetValue(_fake, out float _since))
                {
                    fakeAwakeSince[_fake] = Time.time;
                    continue;
                }

                if (Time.time - _since >= options.fakeSleepDelay)
                {
                    fakeAwakeSince.Remove(_fake);
                    Journal.Record("fake.sleep", string.Format(CultureInfo.InvariantCulture, "{0} after {1:0.0}s", _fake.role?.roleName, Time.time - _since));
                    _fake.SleepCharacterServerRpc(); // what the game's own fake skip calls, just earlier
                }
            }
        }

        // Per-phase bookkeeping only: the runner records the phase change and captures it.
        private void OnStateEntered(GameState _state)
        {
            EndAwakeningLayer();
            CurrentState = _state;
            CurrentStateRealSince = Time.realtimeSinceStartup;
            stateEnterGameTime = Time.time;
            turns.Clear();
            votedThisState.Clear();
            chatSentThisState.Clear();
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
            // Plus the in-game desync tripwire's public projection (roster + pseudos, characters, flags, game state,
            // roles, power lists): one hash per component, so a mismatch names the system that diverged. During the
            // awakening, players act while the processes sample (awake flags, uses left, power effects move), so a
            // one-shot sample cannot be compared there: those components are left to the in-game tripwire, which
            // re-checks after the state settles and logs [DESYNC] (an error, failing noErrors) only if it persists.
            bool _volatilePhase = _state is AwakeningState;
            var _projection = PublicStateProjectionBuilder.Build(
                CompositionRoot.For(networkManager).LobbyPlayerInfoHolder, characterManager, _gameManager);
            _canonical += " # " + string.Join(";", DesyncDigest.ComputeComponentHashes(_projection)
                .Where(_kv => !_volatilePhase || (_kv.Key != PublicStateProjectionBuilder.CharacterFlags &&
                                                  _kv.Key != PublicStateProjectionBuilder.Powers))
                .OrderBy(_kv => _kv.Key, StringComparer.Ordinal)
                .Select(_kv => $"{_kv.Key}={_kv.Value:x16}"));
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
            if (networkManager.IsServer && CurrentState is AwakeningState _awakening)
            {
                TrackAwakeningLayer(_awakening);
                if (options.fastFakes)
                {
                    SleepFakesEarly();
                }
            }

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
                    _p && !_turn.tried.Contains(_p) && !_p.IsPassive && SafeCanUse(_p));
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
                // Kept until the next power starts: visual-picker picks happen after StartUse returns.
                autopilot.CurrentPowerName = _next.powerName.ToString();
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

                Character _focus = string.IsNullOrEmpty(options.voteFocusRole) ? null : _targets.FirstOrDefault(_t =>
                    _t.role != null && _t.role.roleName.ToString().IndexOf(options.voteFocusRole, StringComparison.OrdinalIgnoreCase) >= 0);
                ulong _targetId = (_focus != null ? _focus : policy.Choose(_targets, "vote")).ownerClientId.Value;
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
            // shouldActivate is only set on the SERVER's state instance (ChainingManager); a client only receives the
            // Mage id (SetMageCharacterRpc to all). A client Mage therefore acts on the id and lets the server decide.
            bool _activated = networkManager.IsServer ? _portal.shouldActivate : _mageId == networkManager.LocalClientId;
            if (!_activated || !controlledIds.Contains(_mageId))
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

            Card _card = policy.Choose(autopilot.Focus(_cards, _c => _c.characterInfo), "picker.card");
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

        // Chat lever: once per phase (after the think delay), each controlled player writes one line in every private
        // channel it belongs to. Server side (host seat + simulated bots) the membership is the server's own record and
        // the line goes through the server entry point with the player as sender (as an intercepted bot send does);
        // a real client writes like its chat panel does (active channel + TrySendChatMessage). The host also journals
        // each private channel's members whenever they change, so a tool can tell who should have received what.
        private void UpdateChat(GameManager _gameManager, GameState _state)
        {
            if (chatManager == null || _state is LobbyState || Time.time - stateEnterGameTime < options.thinkDelay)
            {
                return;
            }

            string _phase = $"{_gameManager.currentGameStateIndex.Value}/{_gameManager.currentDay}";
            if (networkManager.IsServer)
            {
                RecordChatMembers(_gameManager.currentDay);
            }

            foreach (ulong _id in controlledIds.OrderBy(_id => _id))
            {
                IEnumerable<int> _channels = networkManager.IsServer
                    ? chatManager.ServerChannelsOf(_id)
                    : chatManager.discoveredChatIds.Where(_c => _c != (int)ChatWindowIDs.General && _c != (int)ChatWindowIDs.Server)
                        .OrderBy(_c => _c).ToList();
                foreach (int _chatId in _channels)
                {
                    if (!chatSentThisState.Add($"{_id}:{_chatId}"))
                    {
                        continue;
                    }

                    string _token = $"ap:{_id}:{++chatSequence}";
                    string _text = $"[{_token}] autoplay {_phase}";
                    if (networkManager.IsServer)
                    {
                        chatManager.SendChatMessageServerRpc(new ChatMessage(_id, new Unity.Collections.FixedString512Bytes(_text), _chatId));
                    }
                    else
                    {
                        chatManager.ChangeActiveChat(_chatId);
                        chatManager.TrySendChatMessage(_text);
                    }
                    Journal.Record("chat.sent", $"chat={_chatId} from={_id} token={_token} phase={_phase}");
                }
            }
        }

        private void RecordChatMembers(int _day)
        {
            var _channels = new SortedSet<int>();
            foreach (Character _character in characterManager.GetCharacters(false))
            {
                if (_character && !_character.isFake)
                {
                    _channels.UnionWith(chatManager.ServerChannelsOf(_character.ownerClientId.Value));
                }
            }

            foreach (int _chatId in _channels)
            {
                string _line = $"chat={_chatId} day={_day} members={string.Join(",", chatManager.ServerMembersOf(_chatId))}";
                if (!chatMembersRecorded.TryGetValue(_chatId, out string _previous) || _previous != _line)
                {
                    chatMembersRecorded[_chatId] = _line;
                    Journal.Record("chat.members", _line);
                }
            }
        }

        private void OnChatReceived(ChatMessage _message)
        {
            if (!running || _message == null)
            {
                return;
            }

            string _text = _message.message.ToString();
            int _start = _text.IndexOf("[ap:", StringComparison.Ordinal);
            int _end = _start >= 0 ? _text.IndexOf(']', _start) : -1;
            string _token = _end > _start ? _text.Substring(_start + 1, _end - _start - 1) : "-";
            Journal.Record("chat.recv", $"chat={_message.chatId} from={_message.senderClientId} token={_token} text={_text}");
        }

        private IEnumerable<string> DescribeKnowledge()
        {
            if (revealer == null || !characterManager)
            {
                yield break;
            }

            List<ulong> _targets = characterManager.GetCharacters(false)
                .Where(_c => _c && !_c.isFake).Select(_c => _c.ownerClientId.Value).OrderBy(_id => _id).ToList();
            foreach (ulong _viewer in controlledIds.OrderBy(_id => _id))
            {
                foreach (ulong _target in _targets.Where(_t => _t != _viewer))
                {
                    CharacterInfoReveal _info = revealer.GetCharacterInfo(_target, _viewer);
                    if (_info.isRoleRevealed == RevealLevel.False && _info.isCorruptRevealed == RevealLevel.False &&
                        _info.forceCorruptOnRoleRevealed == RevealLevel.False && _info.isHacked == RevealLevel.False)
                    {
                        continue;
                    }
                    yield return $"{_viewer}>{_target} role={(int)_info.isRoleRevealed} corrupt={(int)_info.isCorruptRevealed} " +
                                 $"force={(int)_info.forceCorruptOnRoleRevealed} hacked={(int)_info.isHacked}";
                }
            }
        }

        private void OnKnowledgeChanged()
        {
            if (!running)
            {
                return;
            }

            foreach (string _line in DescribeKnowledge())
            {
                string _key = _line.Substring(0, _line.IndexOf(' '));
                if (!knownLevels.TryGetValue(_key, out string _previous) || _previous != _line)
                {
                    knownLevels[_key] = _line;
                    Journal.Record("knowledge", _line);
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
            public string[] knowledge;
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
                knowledge = DescribeKnowledge().ToArray(),
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
