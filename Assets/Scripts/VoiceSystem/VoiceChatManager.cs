#region

using System;
using System.Collections.Generic;
using System.IO;
using Characters;
using ChatSystem;
using CorruptionDuPortail.Domain;
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
    /// In-game voice between the players (board task T22), carried by Steam Voice. Hold the push-to-talk key to speak.
    /// Each client compresses its microphone with Steam and sends the packets to the server, unreliable; the server
    /// picks the listeners (<see cref="VoiceRouting"/>: everyone by day, at night only the players sharing a private
    /// chat channel, i.e. the anomalies together and the messager with his correspondent) and forwards the packets to
    /// them only, so no client ever receives a voice it must not hear. Each listener decodes with Steam and plays one
    /// FMOD voice per speaker (<see cref="VoicePlayback"/>). Without Steam (Relay build) everything stays idle.
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

        /// <summary>Raised when the local microphone opens / closes.</summary>
        public event Action<bool> onTransmittingChanged;

        /// <summary>Raised when a remote player's voice starts / stops coming out (seat, speaking).</summary>
        public event Action<ulong, bool> onSpeakingChanged;

        private void Awake()
        {
            Assert.IsNotNull(_chatManager, "VoiceChatManager._chatManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_gameManager, "VoiceChatManager._gameManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_characterManager, "VoiceChatManager._characterManager is not wired — wire it in GameScene (the composition root).");
            _codec ??= new SteamVoiceCodec();
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
                _playback?.Dispose(); // null = FMOD failed for that speaker
            }
            _playbacks.Clear();
            _lastSequence.Clear();
            base.OnNetworkDespawn();
        }

        private void Update()
        {
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
            Keyboard _keyboard = Keyboard.current;
            if (_keyboard == null || !_keyboard[_pushToTalkKey].isPressed || IsTyping())
            {
                return false;
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
            if (_codec != null && _codec.IsAvailable)
            {
                _codec.Recording = _transmitting;
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
                return;
            }

            // Rejoin: a rejoined player's new connection speaks as his seat.
            ulong _speaker = _characterManager.SeatOfTransport(_params.Receive.SenderClientId);
            foreach (ulong _listener in ListenersOf(_speaker))
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
                _playbacks[_speaker] = _playback; // null stays cached: FMOD failed once, no retry every packet
            }
            return _playback;
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
            }
        }
    }
}
