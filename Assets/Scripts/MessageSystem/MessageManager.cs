using System;
using System.Collections.Generic;
using Characters;
using GameLogic;
using RoleTarget;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace MessageSystem
{
    public class MessageManager : NetworkBehaviour
    {
        // Story 10.4 (Epic 10 / D4): recorded-callers-only façade. The send flow (SendMessagePanel) now
        // resolves this manager through the composition root (lane C); the only remaining direct readers
        // are the two unregistered UI leaves (AwakeningRecapMessages, AnonymousRevealedMessagesComponent),
        // which keep the global until the Epic 12.2 UI pass. Guard #1 forbids the qualified instance
        // accessor in the migrated set (this manager itself uses the bare `instance` self-ref below).
        public static MessageManager instance; // recorded §4 census survivor (12.3 strategy B), whitelisted in StaticSingletonCensusGuardTests
        
        public NetworkList<MessageInfo> revealedMessages = new();
        public NetworkList<MessageInfo> messagesToReveal = new();

        // Per-turn aggregate journal (corrupted count + Robot-targeting count), recorded server-side at each
        // awakening's end alongside the message reveal. Replicated for the message-journal UI (one entry/day).
        public NetworkList<TurnStat> turnStats = new();

        // Story 8.3 lane A: scene-wired GameManager, narrowed to the loop slice (IGameLoop) for currentDay.
        [SerializeField] private GameManager gameManager;
        private IGameLoop Loop => gameManager;

        // Scene-wired direct references (like RobotBoardInfo / CorruptionBoardInfo) — available at Awake, so the
        // per-turn recorder never races against NGO spawn order. (An earlier spawn-time resolution through the
        // composition root was racy: when these came back null at spawn, turnStats stayed empty and the night
        // journal showed nothing. A serialized reference is injection, not the forbidden static locator.)
        [SerializeField] private CharacterManager characterManager;
        [SerializeField] private RoleTargetSystem roleTargetSystem;
        private ICharacterQuery CharacterQuery => characterManager;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            Assert.IsNotNull(gameManager, "MessageManager.gameManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(characterManager, "MessageManager.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(roleTargetSystem, "MessageManager.roleTargetSystem is not wired — wire it in GameScene (the composition root).");
        }

        // Server-only: snapshot this turn's corrupted count and Robot-targeting count. Called from the awakening
        // RECAP (AwakeningRecapMessages.ShowEvent) — a moment guaranteed to run. Recorded BEFORE RoleTargetSystem
        // clears its list at the next awakening's start; currentDay is still this turn's day (it increments only
        // on onNewDayPassed), so the record's day matches the turn's messages.
        public void RecordCurrentTurnStat()
        {
            Assert.IsTrue(IsServer, $"{nameof(RecordCurrentTurnStat)} can only be called on the server.");

            List<Character> _characters = CharacterQuery.GetCharacters();
            List<CharacterFactionState> _states = new(_characters.Count);
            Character _robot = null;
            foreach (Character _character in _characters)
            {
                _states.Add(new CharacterFactionState(_character.role.factionType, _character.isFake, _character.isCorrupted.Value));
                if (_character.role.roleID == RoleID.Robot)
                {
                    _robot = _character;
                }
            }

            bool _hasRobot = _robot;
            int _robotTargeterCount = _hasRobot
                ? roleTargetSystem.GetAllTargetersForTarget(_robot.ownerClientId.Value).Count
                : 0;
            turnStats.Add(TurnStatCalculator.Compute(Loop.currentDay, _states, _hasRobot, _robotTargeterCount));
        }

        public override void OnNetworkDespawn()
        {
            if (instance == this)
            {
                instance = null;
            }

            base.OnNetworkDespawn();
        }

        [Rpc(SendTo.Server)]
        public void SendMessageRpc(ulong _sender, FixedString512Bytes _message)
        {
            messagesToReveal.Add(new MessageInfo(_sender, _message, Loop.currentDay));
        }

        public void RevealAllMessage()
        {
            Assert.IsTrue(IsServer, $"{nameof(RevealAllMessage)} can only be called on the server.");
            foreach (var _messageInfo in messagesToReveal)
            {
                revealedMessages.Add(_messageInfo);
            }
            messagesToReveal.Clear();
        }
    }
    
    [Serializable]
    public struct MessageInfo : INetworkSerializable, IEquatable<MessageInfo>
    {
        public ulong senderClientId;
        public FixedString512Bytes message;
        public int day;
        
        public MessageInfo(ulong _senderClientId, FixedString512Bytes _message, int _day)
        {
            message = _message;
            senderClientId = _senderClientId;
            day = _day;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref message);
            _serializer.SerializeValue(ref senderClientId);
            _serializer.SerializeValue(ref day);
        }

        public bool Equals(MessageInfo _other)
        {
            return senderClientId == _other.senderClientId && message.Equals(_other.message) && day == _other.day;
        }
    }
}