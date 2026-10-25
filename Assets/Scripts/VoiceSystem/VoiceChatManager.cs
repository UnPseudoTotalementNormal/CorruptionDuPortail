#region

using System;
using System.Collections.Generic;
using System.IO;
using Characters;
using ChatSystem;
using CorruptionDuPortail.Domain;
using Diagnostics.Logs;
using Extensions;
using FMODUnity;
using Avatars;
using GameLogic;
using GameLogic.GameStates;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

#endregion

namespace VoiceSystem
{
    /// <summary>
    /// In-game voice between the players (board task T22), carried by Steam Voice. Hold the push-to-talk key to speak,
    /// or keep the microphone open (<see cref="VoiceOptions"/>); a soft click marks it opening and closing (mockups
    /// vocal v1, B3: nothing on screen).
    /// Each client compresses its microphone with Steam and sends the packets to the server, unreliable; the server
    /// picks the listeners (<see cref="VoiceRouting"/>: everyone by day, at night only the players sharing a private
    /// chat channel, i.e. the anomalies together and the messager with his correspondent) and forwards the packets to
    /// them only, so no client ever receives a voice it must not hear. Each listener decodes with Steam and plays one
    /// FMOD voice per speaker (<see cref="VoicePlayback"/>) and hands its level to the speaker's avatar, whose cat opens
    /// its mouth with it (mockups vocal v2: W3, R1, M1; local to each listener). Without Steam (Relay build) everything stays idle.
    /// Lives on the ChatManager object of GameScene (lane A: the managers below are wired in the scene).
    /// </summary>
    public class VoiceChatManager : NetworkBehaviour
    {
        // Under one unreliable datagram (UTP / Steam relay ~1200 bytes of payload after headers).
        public const int MaxPacketBytes = 1000;
        private const float SendInterval = 0.06f;

        [SerializeField] private ChatManager _chatManager;
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private CharacterManager _characterManager;
        [Tooltip("Held to speak.")]
        [SerializeField] private Key _pushToTalkKey = Key.V;
        [SerializeField] private VoiceOptions _options;
        [SerializeField] private EventReference _micOpenSound;
        [SerializeField] private EventReference _micCloseSound;
        [Tooltip("Finds the speaker's cat, which opens its mouth while his voice plays.")]
        [SerializeField] private AvatarManager _avatarManager;

        private IGameStateQuery Query => _gameManager;

        private IVoiceCodec _codec;
        private Func<ulong, int, VoicePlayback> _createPlayback = VoicePlayback.Create;
        private readonly MemoryStream _pending = new();
        private readonly MemoryStream _captured = new();
        private readonly MemoryStream _pcm = new();
        private float _pendingSince;
        private ushort _nextSequence;

        private readonly Dictionary<ulong, VoicePlayback> _playbacks = new();
        private readonly Dictionary<ulong, ushort> _lastSequence = new();

        /// <summary>The local microphone is open and its voice is being sent.</summary>
        public bool IsTransmitting { get; private set; }

        /// <summary>The key held to speak, shown by the key hints.</summary>
        public Key PushToTalkKey => _pushToTalkKey;

        /// <summary>Holding <see cref="PushToTalkKey"/> would open the microphone now: voice available (Steam), push to
        /// talk (not the always-open mic) and the player may speak at this moment (by day, or a private channel at night).</summary>
        public bool PushToTalkReady =>
            IsSpawned && _codec != null && _codec.IsAvailable && !_options.OpenMic
            && VoiceRouting.MaySpeak(IsNight(), HasPrivateChannel());

        /// <summary>Raised when the local microphone opens / closes.</summary>
        public event Action<bool> onTransmittingChanged;

        /// <summary>Raised when a remote player's voice starts / stops coming out (seat, speaking).</summary>
        public event Action<ulong, bool> onSpeakingChanged;

        private void Awake()
        {
            Assert.IsNotNull(_chatManager, "VoiceChatManager._chatManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_gameManager, "VoiceChatManager._gameManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_characterManager, "VoiceChatManager._characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_options, "VoiceChatManager._options is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_avatarManager, "VoiceChatManager._avatarManager is not wired — wire it in GameScene (the composition root).");
            _codec ??= new SteamVoiceCodec();
            _options.OnChanged += ApplyVolume;
        }

        public override void OnDestroy()
        {
            if (_options != null)
            {
                _options.OnChanged -= ApplyVolume;
            }
            base.OnDestroy();
        }

        /// <summary>Tests: replaces Steam Voice and the FMOD output.</summary>
        internal void UseCodec(IVoiceCodec _testCodec, Func<ulong, int, VoicePlayback> _testPlayback)
        {
            _codec = _testCodec;
            _createPlayback = _testPlayback;
        }

        public override void OnNetworkDespawn()
        {
            SetTransmitting(false);
            foreach (VoicePlayback _playback in _playbacks.Values)
            {
                if (_playback != null)
                {
                    ShowVoiceLevel(_playback.Seat, 0f);
                }
                _playback?.Dispose(); // null = FMOD failed for that speaker
            }
            _playbacks.Clear();
            _lastSequence.Clear();
            _logLastListeners.Clear();
            _logFailingSpeakers.Clear();
            _logNoPlaybackSpeakers.Clear();
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            LogAvailabilityEdge();
            if (!IsSpawned || _codec == null || !_codec.IsAvailable)
            {
                return;
            }

            SetTransmitting(WantsToTransmit());
            Capture();
            TickPlaybacks();
        }

        // ---- Sending ----

        private bool WantsToTransmit()
        {
            if (!_options.OpenMic)
            {
                Keyboard _keyboard = Keyboard.current;
                if (_keyboard == null || !_keyboard[_pushToTalkKey].isPressed || IsTyping())
                {
                    return false;
                }
            }
            return VoiceRouting.MaySpeak(IsNight(), HasPrivateChannel());
        }

        // The push-to-talk key is a letter: typing it in the chat must not open the microphone.
        private static bool IsTyping()
        {
            GameObject _selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return _selected != null && _selected.TryGetComponent(out TMP_InputField _field) && _field.isFocused;
        }

        private bool HasPrivateChannel()
        {
            foreach (int _chatId in _chatManager.discoveredChatIds)
            {
                if (_chatId != (int)ChatWindowIDs.General && _chatId != (int)ChatWindowIDs.Server)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsNight() => Query.GetGameState(Query.currentGameStateIndex.Value) is AwakeningState;

        private void SetTransmitting(bool _transmitting)
        {
            if (_transmitting == IsTransmitting)
            {
                return;
            }
            IsTransmitting = _transmitting;
            GameLog.Guarded(() => GameLog.Info("voice", "voice.mic", "on", _transmitting,
                "mode", _options != null && _options.OpenMic ? "open" : "push-to-talk"));
            if (_codec != null && _codec.IsAvailable)
            {
                _codec.Recording = _transmitting;
            }
            if (IsSpawned)
            {
                (_transmitting ? _micOpenSound : _micCloseSound).TryPlayOneShot();
            }
            onTransmittingChanged?.Invoke(_transmitting);
        }

        // Steam keeps handing out the tail of the voice for a moment after the key is released: read it until empty.
        private void Capture()
        {
            _captured.SetLength(0);
            int _read = _codec.ReadCaptured(_captured);
            if (_read > 0)
            {
                // Steam's packets cannot be cut: send what is pending before it would overflow one datagram.
                if (_pending.Length > 0 && _pending.Length + _read > MaxPacketBytes)
                {
                    Flush();
                }
                if (_pending.Length == 0)
                {
                    _pendingSince = Time.unscaledTime;
                }
                _pending.Write(_captured.GetBuffer(), 0, _read);
            }
            if (_pending.Length > 0 && Time.unscaledTime - _pendingSince >= SendInterval)
            {
                Flush();
            }
        }

        private void Flush()
        {
            if (_pending.Length <= MaxPacketBytes)
            {
                SendVoice(_pending.ToArray());
            }
            else
            {
                Debug.LogWarning($"[VOICE] {_pending.Length} bytes captured at once: over one datagram, dropped.");
            }
            _pending.SetLength(0);
        }

        /// <summary>Sends one packet of compressed voice to the server, which forwards it to the listeners.</summary>
        internal void SendVoice(byte[] _packet) => SendVoiceServerRpc(_packet, _nextSequence++);

        // ---- Server routing ----

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        private void SendVoiceServerRpc(byte[] _voice, ushort _sequence, RpcParams _params = default)
        {
            if (_voice == null || _voice.Length == 0 || _voice.Length > MaxPacketBytes)
            {
                if (_voice != null && _voice.Length > MaxPacketBytes)
                {
                    LogOversizedPacket(_params.Receive.SenderClientId, _voice.Length);
                }
                return;
            }

            // Rejoin: a rejoined player's new connection speaks as his seat.
            ulong _speaker = _characterManager.SeatOfTransport(_params.Receive.SenderClientId);
            List<ulong> _listeners = ListenersOf(_speaker);
            LogChannelIfChanged(_speaker, _listeners);
            foreach (ulong _listener in _listeners)
            {
                ReceiveVoiceRpc(_speaker, _sequence, _voice, _characterManager.GetSafeRpcTarget(_listener));
            }
        }

        /// <summary>Server: the seats that hear <paramref name="_speaker"/> right now (real connections only).</summary>
        internal List<ulong> ListenersOf(ulong _speaker)
        {
            var _connectedSeats = new List<ulong>();
            foreach (ulong _connection in NetworkManager.ConnectedClientsIds)
            {
                _connectedSeats.Add(_characterManager.SeatOfTransport(_connection));
            }
            return VoiceRouting.Listeners(_speaker, IsNight(), _connectedSeats,
                _chatManager.ServerChannelsOf, _chatManager.ServerMembersOf);
        }

        // ---- Receiving ----

        // The server never sends a voice back to its speaker.
        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
        private void ReceiveVoiceRpc(ulong _speaker, ushort _sequence, byte[] _voice, RpcParams _params = default)
        {
            if (_codec == null || !_codec.IsAvailable || _voice == null)
            {
                return;
            }

            VoicePlayback _playback = PlaybackOf(_speaker);
            // A late packet is dropped; after a silence anything goes (the speaker may have rejoined, counting from 0).
            if (_playback != null && _playback.IsSpeaking && _lastSequence.TryGetValue(_speaker, out ushort _last)
                && !VoiceRouting.IsNewerSequence(_sequence, _last))
            {
                return;
            }
            _lastSequence[_speaker] = _sequence;

            int _decoded = _codec.Decode(_voice, _voice.Length, _pcm);
            LogReceiveEdge(_speaker, _decoded > 0, _playback != null);
            if (_decoded <= 0 || _playback == null)
            {
                return;
            }
            bool _wasSpeaking = _playback.IsSpeaking;
            _playback.Push(_pcm.GetBuffer(), _decoded);
            if (!_wasSpeaking && _playback.IsSpeaking)
            {
                onSpeakingChanged?.Invoke(_speaker, true);
            }
        }

        private VoicePlayback PlaybackOf(ulong _speaker)
        {
            if (!_playbacks.TryGetValue(_speaker, out VoicePlayback _playback))
            {
                _playback = _createPlayback(_speaker, _codec.SampleRate);
                _playback?.SetVolume(_options.Volume);
                _playbacks[_speaker] = _playback; // null stays cached: FMOD failed once, no retry every packet
            }
            return _playback;
        }

        private void ApplyVolume()
        {
            foreach (VoicePlayback _playback in _playbacks.Values)
            {
                _playback?.SetVolume(_options.Volume);
            }
        }

        // ---- Playtest logs (edges only: the voice runs every frame / every packet) ----
        // Every call to these helpers is compiled out in a build without the logs.

        private bool _logAvailabilityKnown;
        private bool _logWasAvailable;
        private readonly Dictionary<ulong, int> _logLastListeners = new();
        private readonly HashSet<ulong> _logFailingSpeakers = new();
        private readonly HashSet<ulong> _logNoPlaybackSpeakers = new();
        private int _logOversizedCount;
        private const int LogOversizedEvery = 100;

        [System.Diagnostics.Conditional(GameLog.Define)]
        private void LogAvailabilityEdge()
        {
            if (!IsSpawned)
            {
                _logAvailabilityKnown = false;
                return;
            }
            bool _available = _codec != null && _codec.IsAvailable;
            if (_logAvailabilityKnown && _available == _logWasAvailable)
            {
                return;
            }
            _logAvailabilityKnown = true;
            _logWasAvailable = _available;
            if (_available)
            {
                GameLog.Info("voice", "voice.available");
            }
            else
            {
                GameLog.Warn("voice", "voice.unavailable", "codec", _codec != null);
            }
        }

        [System.Diagnostics.Conditional(GameLog.Define)]
        private void LogChannelIfChanged(ulong _speaker, List<ulong> _listeners)
        {
            // Runs for every voice packet: no closure, no string, only a hash of the listener set.
            try
            {
                int _key = 17;
                if (_listeners != null)
                {
                    foreach (ulong _listener in _listeners) _key = _key * 31 + _listener.GetHashCode();
                }
                if (_logLastListeners.TryGetValue(_speaker, out int _last) && _last == _key)
                {
                    return;
                }
                _logLastListeners[_speaker] = _key;
                GameLog.Info("voice", "voice.channel", "speaker", _speaker, "night", IsNight(),
                    "listeners", _listeners != null ? new List<ulong>(_listeners) : null);
            }
            catch (System.Exception)
            {
                // logging never breaks the game
            }
        }

        [System.Diagnostics.Conditional(GameLog.Define)]
        private void LogOversizedPacket(ulong _sender, int _bytes)
        {
            _logOversizedCount++;
            if (_logOversizedCount == 1 || _logOversizedCount % LogOversizedEvery == 0)
            {
                GameLog.Warn("voice", "voice.packet.refused", "connection", _sender, "bytes", _bytes,
                    "max", MaxPacketBytes, "count", _logOversizedCount);
            }
        }

        [System.Diagnostics.Conditional(GameLog.Define)]
        private void LogReceiveEdge(ulong _speaker, bool _decodedOk, bool _hasPlayback)
        {
            // Once per speaker and session: an empty decode may be benign, a broken stream would repeat every packet.
            if (!_decodedOk && _logFailingSpeakers.Add(_speaker))
            {
                GameLog.Warn("voice", "voice.decode.failed", "speaker", _speaker);
            }
            if (!_hasPlayback && _logNoPlaybackSpeakers.Add(_speaker))
            {
                GameLog.Warn("voice", "voice.playback.failed", "speaker", _speaker);
            }
        }

        private void TickPlaybacks()
        {
            foreach (VoicePlayback _playback in _playbacks.Values)
            {
                if (_playback == null)
                {
                    continue;
                }
                bool _wasSpeaking = _playback.IsSpeaking;
                _playback.Tick();
                if (_wasSpeaking && !_playback.IsSpeaking)
                {
                    onSpeakingChanged?.Invoke(_playback.Seat, false);
                }
                if (_wasSpeaking || _playback.IsSpeaking)
                {
                    ShowVoiceLevel(_playback.Seat, _playback.Level);
                }
            }
        }

        // The speaker's avatar may be missing (not spawned yet, left): the level is simply not shown.
        private void ShowVoiceLevel(ulong _seat, float _level)
        {
            PlayerAvatar _avatar = _avatarManager != null ? _avatarManager.GetAvatar(_seat) : null;
            if (_avatar != null)
            {
                _avatar.SetVoiceLevel(_level);
            }
        }
    }
}
