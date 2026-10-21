using System.Collections;
using System.Collections.Generic;
using System.IO;
using Characters;
using ChatSystem;
using GameLogic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VoiceSystem;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// T22 in-game voice over the real 2-NM loopback: a packet sent by one peer travels client → server → listener
    /// through the two unreliable RPCs and reaches the other peer, never its own speaker. Steam Voice and the FMOD
    /// output are replaced by a recording codec (no Steam client in tests). Who hears whom at night is pinned by the
    /// Domain tests (VoiceRoutingTests); this suite proves the wire path and the server routing by day.
    /// </summary>
    public class VoiceChatReplicationTests : MultiClientGameFixture
    {
        private const uint VoicePrefabHash = 0xC0DE0601u;

        private GameObject _voicePrefabGo, _placeholderGo;
        private NetworkObject _voicePrefabNo;
        private ChatManager _placeholderChat;
        private VoiceOptions _voiceOptions;
        private VoiceChatManager _hostVoice, _clientVoice;
        private RecordingCodec _hostCodec, _clientCodec;

        private sealed class RecordingCodec : IVoiceCodec
        {
            public readonly List<byte[]> Decoded = new();

            public bool IsAvailable => true;
            public int SampleRate => 24000;
            public bool Recording { get; set; }

            public int ReadCaptured(MemoryStream into) => 0;

            public int Decode(byte[] compressed, int length, MemoryStream pcm)
            {
                var _copy = new byte[length];
                System.Array.Copy(compressed, _copy, length);
                Decoded.Add(_copy);
                pcm.SetLength(0);
                pcm.Write(_copy, 0, length);
                return length;
            }
        }

        protected override void BuildExtraNetworkPrefabs(List<GameObject> _templates)
        {
            // Inactive placeholders: their Awake never runs (no singleton claims), they only satisfy the lane-A
            // asserts of the voice manager's Awake. The host instance is rewired to the real managers after spawn.
            _placeholderGo = new GameObject("VoicePlaceholders");
            _placeholderGo.SetActive(false);
            _placeholderChat = _placeholderGo.AddComponent<ChatManager>();
            var _placeholderGm = _placeholderGo.AddComponent<GameManager>();
            var _placeholderCm = _placeholderGo.AddComponent<CharacterManager>();

            _voicePrefabGo = new GameObject("VoicePrefab");
            _voicePrefabNo = _voicePrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_voicePrefabNo, VoicePrefabHash);
            MarkAsNonSceneObject(_voicePrefabNo);
            _voicePrefabGo.SetActive(false); // wire before Awake
            var _template = _voicePrefabGo.AddComponent<VoiceChatManager>();
            ReflectionHelper.SetPrivateField(_template, "_chatManager", _placeholderChat);
            ReflectionHelper.SetPrivateField(_template, "_gameManager", _placeholderGm);
            ReflectionHelper.SetPrivateField(_template, "_characterManager", _placeholderCm);
            _voiceOptions = ScriptableObject.CreateInstance<VoiceOptions>();
            _voiceOptions.SetOpenMic(false, false); // hold-to-talk whatever this PC saved: nobody holds V here
            ReflectionHelper.SetPrivateField(_template, "_options", _voiceOptions);
            _voicePrefabGo.SetActive(true);
            _templates.Add(_voicePrefabGo);
        }

        [UnitySetUp]
        public IEnumerator VoiceSetUp()
        {
            _hostVoice = HostNm.SpawnManager.InstantiateAndSpawn(_voicePrefabNo, destroyWithScene: true)
                .GetComponent<VoiceChatManager>();
            ReflectionHelper.SetPrivateField(_hostVoice, "_gameManager", HostGm);
            ReflectionHelper.SetPrivateField(_hostVoice, "_characterManager", HostCm);
            _hostCodec = new RecordingCodec();
            _hostVoice.UseCodec(_hostCodec, (_, _) => null);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_hostVoice, 5f);

            ulong _netId = _hostVoice.NetworkObjectId;
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => ClientNm.SpawnManager.SpawnedObjects.ContainsKey(_netId), 5f,
                "Client NM never spawned its replica of the VoiceChatManager.");
            _clientVoice = ClientNm.SpawnManager.SpawnedObjects[_netId].GetComponent<VoiceChatManager>();
            _clientCodec = new RecordingCodec();
            _clientVoice.UseCodec(_clientCodec, (_, _) => null);
        }

        [UnityTearDown]
        public IEnumerator VoiceTearDown()
        {
            if (HostNm != null && HostNm.IsListening && _hostVoice != null && _hostVoice.IsSpawned)
            {
                _hostVoice.NetworkObject.Despawn(true);
            }
            yield return null;
            if (_placeholderGo != null) Object.Destroy(_placeholderGo);
            if (_voiceOptions != null) Object.Destroy(_voiceOptions);
            _hostVoice = null;
            _clientVoice = null;
        }

        [UnityTest]
        public IEnumerator Day_ClientVoice_ReachesTheHost_NotItsSpeaker()
        {
            var _packet = new byte[] { 1, 2, 3, 4 };
            _clientVoice.SendVoice(_packet);

            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _hostCodec.Decoded.Count > 0, 5f,
                "The host never received the client's voice packet.");
            CollectionAssert.AreEqual(_packet, _hostCodec.Decoded[0]);
            yield return null;
            yield return null;
            CollectionAssert.IsEmpty(_clientCodec.Decoded, "The server must not send a voice back to its speaker.");
        }

        [UnityTest]
        public IEnumerator Day_HostVoice_ReachesTheClient_NotTheHost()
        {
            var _packet = new byte[] { 9, 8, 7 };
            _hostVoice.SendVoice(_packet);

            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _clientCodec.Decoded.Count > 0, 5f,
                "The client never received the host's voice packet.");
            CollectionAssert.AreEqual(_packet, _clientCodec.Decoded[0]);
            CollectionAssert.IsEmpty(_hostCodec.Decoded, "The host must not hear itself.");
        }

        [UnityTest]
        public IEnumerator Server_ListsEveryOtherConnectedSeatByDay()
        {
            CollectionAssert.AreEqual(new[] { ClientNm.LocalClientId }, _hostVoice.ListenersOf(HostNm.LocalClientId));
            CollectionAssert.AreEqual(new[] { HostNm.LocalClientId }, _hostVoice.ListenersOf(ClientNm.LocalClientId));
            yield return null;
        }
    }
}
